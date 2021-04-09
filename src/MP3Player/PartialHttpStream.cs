using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace MP3Player
{
    public class PartialHttpStream : Stream
    {
        private long? _length;
        private long _position;
        private bool _positionChanged;
        private readonly byte[] _readAheadBuffer;
        private int _readAheadLength;
        private int _readAheadOffset;
        
        protected readonly HttpClient HttpClient;
        protected Stream SourceStream;

        public string Url { get; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;

        /// <summary>
        /// Lazy initialized length of the resource.
        /// </summary>
        public override long Length
        {
            get
            {
                if (_length == null)
                {
                    _length = HttpGetLength();
                }
                return _length.Value;
            }
        }

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public PartialHttpStream(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentNullException(nameof(url));
            }

            Url = url;
            HttpClient = new HttpClient();
            _readAheadBuffer = new byte[4096];
        }

        public override void Flush()
        {
            throw new InvalidOperationException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset >= buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            int bytesRead = 0;
            if (_positionChanged || SourceStream?.CanRead != true)
            {
                _positionChanged = false;
                SourceStream?.Dispose();
                var request = new HttpRequestMessage(HttpMethod.Get, Url);
                if (Position > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(Position, Length);
                }
                HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, new CancellationToken()).ContinueWith(
                    response => {
                        if (_length == null && response.Result.Content.Headers.ContentLength > 0)
                        {
                            SetLength(response.Result.Content.Headers.ContentLength ?? 0);
                        }
                        SourceStream = response.Result.Content.ReadAsStreamAsync().Result;
                    }).Wait();
            }

            while (bytesRead < count)
            {
                int readAheadAvailableBytes = _readAheadLength - _readAheadOffset;
                int bytesRequired = count - bytesRead;
                if (readAheadAvailableBytes > 0)
                {
                    int toCopy = Math.Min(readAheadAvailableBytes, bytesRequired);
                    Array.Copy(_readAheadBuffer, _readAheadOffset, buffer, offset + bytesRead, toCopy);
                    bytesRead += toCopy;
                    _readAheadOffset += toCopy;
                }
                else
                {
                    _readAheadOffset = 0;
                    _readAheadLength = SourceStream.Read(_readAheadBuffer, 0, _readAheadBuffer.Length);
                    if (_readAheadLength == 0)
                    {
                        break;
                    }
                }
            }
            _position += bytesRead;
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _positionChanged = true;
            switch (origin)
            {
                case SeekOrigin.End:
                    _position = Length + offset;
                    break;

                case SeekOrigin.Begin:
                    _position = offset;
                    break;

                case SeekOrigin.Current:
                    _position += offset;
                    break;
            }

            return Position;
        }

        public override void SetLength(long value)
        {
            _length = value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        private long HttpGetLength()
        {
            HttpWebRequest request = WebRequest.CreateHttp(Url);
            request.Method = "GET";
            return request.GetResponse().ContentLength;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                HttpClient.CancelPendingRequests();
                HttpClient.Dispose();
                SourceStream?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
