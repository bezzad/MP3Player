using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace MP3Player.Sample
{
    /// <summary>
    /// Basic implementation of the <see cref="ICommand"/>
    /// </summary>
    public class RelayCommand : DelegateCommandBase
    {
        private Action ExecuteMethod { get; }
        private Func<bool> CanExecuteMethod { get; }

        /// <summary>
        /// Returns a disabled command.
        /// </summary>
        public static RelayCommand DisabledCommand { get; } = new RelayCommand(() => { }, () => false);

        /// <summary>
        ///     Constructor
        /// </summary>
        public RelayCommand([NotNull] Action execute, Func<bool> canExecute = null)
        {
            ExecuteMethod = execute ?? throw new ArgumentNullException(nameof(execute));
            CanExecuteMethod = canExecute;
        }


        /// <inheritdoc />
        public override void Execute(object parameter = null)
        {
            if (!CanExecute())
                return;

            IsExecuting = true;
            try
            {
                ExecuteMethod?.Invoke();
            }
            finally
            {
                IsExecuting = false;
            }
        }

        /// <inheritdoc />
        public override bool CanExecute(object parameter = null)
        {
            return !IsExecuting && (CanExecuteMethod?.Invoke() ?? true);
        }
    }
}
