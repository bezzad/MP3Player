using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using MP3Player.Utils;

namespace MP3Player
{
    public class PartialHttpStream : Stream
    {
        private const int BufferChunkSize = 4096;
        private long? _length;
        private long _streamPosition;
        private long _position;
        private int _readAheadLength;
        private readonly byte[] _readAheadBuffer;
        private byte?[] _cache;
        private readonly HttpClient _httpClient;
        private Stream _sourceStream;

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
            _httpClient = new HttpClient();
            _readAheadBuffer = new byte[BufferChunkSize];
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
            while (bytesRead < count)
            {
                if (_cache?[Position] != null)
                {
                    buffer[offset + bytesRead++] = _cache[_position++].Value;
                }
                else
                {
                    SetStreamPosition(Position);
                    _readAheadLength = _sourceStream.Read(_readAheadBuffer, 0, _readAheadBuffer.Length);
                    _streamPosition += _readAheadLength;
                    if (_readAheadLength == 0)
                    {
                        break;
                    }
                    // write to cache
                    _readAheadBuffer.Copy(0, _cache, _position, _readAheadLength);
                }
            }

            return bytesRead;
        }
        
        private void SetStreamPosition(long pos)
        {
            if (_sourceStream == null || _streamPosition != pos)
            {
                _sourceStream?.Dispose();
                _streamPosition = pos;
                var request = new HttpRequestMessage(HttpMethod.Get, Url);
                if (_length != null && _streamPosition > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(_streamPosition, Length);
                }
                _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, new CancellationToken()).ContinueWith(
                    response => {
                        if (_length == null && response.Result.Content.Headers.ContentLength > 0)
                        {
                            SetLength(response.Result.Content.Headers.ContentLength ?? 0);
                        }
                        _sourceStream = response.Result.Content.ReadAsStreamAsync().Result;
                    }).Wait();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
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
            _cache = new byte?[value];
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

        public bool IsFullyLoaded()
        {
            if (_cache?.Length > 0)
            {
                foreach (var aByte in _cache)
                {
                    if (aByte.HasValue == false)
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
                _sourceStream?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
