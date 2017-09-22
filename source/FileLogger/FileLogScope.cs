using System;
using System.Threading;

namespace Karambolo.Extensions.Logging.File
{
    public class FileLogScope
    {
        class DisposableScope : IDisposable
        {
            public void Dispose()
            {
                Current = Current.Parent;
            }
        }

        static AsyncLocal<FileLogScope> _current = new AsyncLocal<FileLogScope>();
        public static FileLogScope Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }

        public static IDisposable Push(object state)
        {
            Current = new FileLogScope(Current, state);
            return new DisposableScope();
        }

        readonly object _state;

        FileLogScope(FileLogScope parent, object state)
        {
            Parent = parent;
            _state = state;
        }

        public FileLogScope Parent { get; }

        public override string ToString()
        {
            return _state?.ToString();
        }
    }
}
