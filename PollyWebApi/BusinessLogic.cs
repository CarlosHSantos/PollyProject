using System;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;
using Polly.Timeout;
using Polly.Wrap;

namespace PollyWebApi
{
    public class BusinessLogic
    {
        private readonly ISyncPolicy _policy;
        private readonly RetryPolicy<int> _retryPolicy;
        private readonly CircuitBreakerPolicy _circuitBreakerPolicy;
        private readonly FallbackPolicy<Account> _fallbackPolicy;
        private readonly IBasicClass _epc;

        public PolicyWrap<Account> _policyWrap { get; }
        public IBasicClass Object { get; }

        public BusinessLogic(ISyncPolicy policy, IBasicClass epc)
        {
            _policy = policy;
            _epc = epc;
        }
        public BusinessLogic(RetryPolicy<int> policy, IBasicClass epc)
        {
            _retryPolicy = policy;
            _epc = epc;
        }

        public BusinessLogic(CircuitBreakerPolicy policy, IBasicClass epc)
        {
            _circuitBreakerPolicy = policy;
            _epc = epc;
        }

        public BusinessLogic(FallbackPolicy<Account> policy, IBasicClass epc)
        {
            _fallbackPolicy = policy;
            _epc = epc;
        }

        public BusinessLogic(PolicyWrap<Account> policyWrap, IBasicClass epc)
        {
            _policyWrap = policyWrap;
            Object = epc;
        }

        public Account CreateNewAccount(string name, int age)
        {
            return _fallbackPolicy.Execute(() => _epc.CreateNewAccount(name, age));
        }

        public Account CreateNewAccountWithPolicyWrap(string name, int age)
        {
            return _policyWrap.Execute(() => _epc.CreateNewAccount(name, age));
        }

        public int CallSomeCodeThatNeedsToBeRetried()
        {
            return _retryPolicy.Execute(() => _epc.GetSomeNumber());
        }

        public int CallSomeSlowBadCode()
        {
            try
            {
                return _policy.Execute(() => _epc.GetSomeNumber());
            }
            catch (TimeoutRejectedException)
            {
                return 999;
            }
        }

        public int CallSomeCachedCode()
        {
            Random rnd = new Random(1);
            return _policy.Execute(() => rnd.Next(100000));
        }

        public string GetHelloMessage()
        {
            try
            {
                Console.WriteLine($"Circuit State: {_circuitBreakerPolicy.CircuitState}");
                return _circuitBreakerPolicy.Execute<string>(() =>
                {
                    throw new Exception("Exception in Hello Message method");
                });
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

    }
}
