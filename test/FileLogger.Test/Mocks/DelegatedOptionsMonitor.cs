using System;
using Microsoft.Extensions.Options;

namespace Karambolo.Extensions.Logging.File.Test.Mocks
{
    internal class DelegatedOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    {
        private readonly Func<string, TOptions> _getCurrentValue;
        private Action<TOptions, string> _listeners;
        private readonly object _gate;

        public DelegatedOptionsMonitor(Func<string, TOptions> getCurrentValue)
        {
            _getCurrentValue = getCurrentValue;
            _gate = new object();
        }

        public TOptions CurrentValue => _getCurrentValue(Options.DefaultName);

        public TOptions Get(string name) => _getCurrentValue(name ?? Options.DefaultName);

        public void Reload(string name = null)
        {
            if (name == null)
                name = Options.DefaultName;

            _listeners?.Invoke(_getCurrentValue(name), name);
        }

        public IDisposable OnChange(Action<TOptions, string> listener)
        {
            lock (_gate)
                _listeners = (Action<TOptions, string>)Delegate.Combine(_listeners, listener);

            return new ChangeRegistrationToken(this, listener);
        }

        private class ChangeRegistrationToken : IDisposable
        {
            private DelegatedOptionsMonitor<TOptions> _owner;
            private Action<TOptions, string> _listener;

            public ChangeRegistrationToken(DelegatedOptionsMonitor<TOptions> owner, Action<TOptions, string> listener)
            {
                _owner = owner;
                _listener = listener;
            }

            public void Dispose()
            {
                if (_listener != null)
                {
                    lock (_owner._gate)
                        _owner._listeners = (Action<TOptions, string>)Delegate.Remove(_owner._listeners, _listener);

                    _listener = null;
                    _owner = null;
                }
            }
        }
    }
}
