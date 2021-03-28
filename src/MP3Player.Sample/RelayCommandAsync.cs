using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MP3Player.Sample
{
    /// <summary>
    /// Basic implementation of the <see cref="ICommand"/>
    /// </summary>
    public class RelayCommandAsync : DelegateCommandBase, ICommand
    {
        private Func<Task> ExecuteMethod { get; }
        private Func<bool> CanExecuteMethod { get; }

        /// <summary>
        /// Returns a disabled command.
        /// </summary>
        public static RelayCommandAsync DisabledCommand { get; } = new RelayCommandAsync(() => CompletedTask, () => false);

        /// <summary>
        ///     Constructor
        /// </summary>
        public RelayCommandAsync([NotNull] Func<Task> execute, Func<bool> canExecute = null)
        {
            ExecuteMethod = execute ?? throw new ArgumentNullException(nameof(execute));
            CanExecuteMethod = canExecute;
        }

        /// <inheritdoc />
        public override bool CanExecute(object parameter = null)
        {
            return !IsExecuting && (CanExecuteMethod?.Invoke() ?? true);
        }

        /// <inheritdoc />
        public override void Execute(object parameter = null)
        {
            _=ExecuteAsync();
        }
        private async Task ExecuteAsync()
        {
            if (CanExecute(null))
            {
                try
                {
                    IsExecuting = true;
                    await ExecuteMethod();
                }
                finally
                {
                    IsExecuting = false;
                }
            }

            RaiseCanExecuteChanged();
        }
    }
}
