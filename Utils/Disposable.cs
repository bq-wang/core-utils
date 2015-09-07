using System;

namespace Utils
{
    /// <summary>
    /// Static class for creating IDisposable
    /// </summary>
    public static class Disposable
    {
        /// <summary>
        /// Gets an empty IDisposable
        /// </summary>
        public static IDisposable Empty = new DelegateDisposer(() => { });

        /// <summary>
        /// Creates a new IDisposable that executes an dispose action when disposed
        /// </summary>
        public static IDisposable Create(Action disposeAction)
        {
            return new DelegateDisposer(disposeAction);
        }
    }

    /// <summary>
    /// Implementation of IDisposable that executes an action on disposal
    /// </summary>
    public class DelegateDisposer : IDisposable
    {
        #region Instance Fields

        private readonly Action _disposeAction;

        #endregion

        #region Constructors

        /// <summary>
        /// Use Disposable.Create() instead to create this instance
        /// </summary>
        internal DelegateDisposer(Action disposeAction)
        {
            _disposeAction = disposeAction;
        }

        #endregion

        #region Implementation of IDisposable

        /// <summary>
        /// Dispose this object
        /// </summary>
        public void Dispose()
        {
            _disposeAction();
        }

        #endregion
    }
}
