using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Polly;
using Polly.Bulkhead;
using Polly.Caching;
using Polly.Caching.Memory;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.RateLimit;
using Polly.Retry;
using Polly.Timeout;
using PollyProject;
using Xunit;

namespace Tests
{
    public class BusinessLogicTest
    {
        [Fact]
        public void Should_Return_999_When_TimeoutRejectedException_Thrown()
        {
            //Arrange 
            Mock<IBasicClass> mockedErrorBasicClass = new Mock<IBasicClass>();
            mockedErrorBasicClass.Setup(e => e.GetSomeNumber()).Returns(0);

            Mock<ISyncPolicy> mockedPolicy = new Mock<ISyncPolicy>();
            mockedPolicy.Setup(p => p.Execute(It.IsAny<Func<int>>())).Throws(new TimeoutRejectedException("Mocked Timeout Exception"));

            BusinessLogic businessLogic = new BusinessLogic(mockedPolicy.Object, mockedErrorBasicClass.Object);

            //Act
            int num = businessLogic.CallSomeSlowBadCode();

            //Assert
            (num % 2).Should().Be(1);
        }

        #region retry

        [Fact]
        public void Should_Return_Odd_When_Retry()
        {
            //Arrange 
            Mock<IBasicClass> mockedErrorBasicClass = new Mock<IBasicClass>();

            Random rnd = new Random(1);

            mockedErrorBasicClass.Setup(e => e.GetSomeNumber()).Returns(() => rnd.Next(10)); // returns 2, 1, 4...

            RetryPolicy<int> policy = Policy.HandleResult<int>(i => i % 2 != 1) // retry if the number is not odd.
                .Retry(3);
            BusinessLogic otherBusinessLogic = new BusinessLogic(policy, mockedErrorBasicClass.Object);

            //Act
            int num = otherBusinessLogic.CallSomeCodeThatNeedsToBeRetried();

            //Assert
            (num % 2).Should().Be(1);
        }

        [Fact]
        public void Should_Return_Odd_When_RetryForever()
        {
            //Arrange 
            Mock<IBasicClass> mockedErrorBasicClass = new Mock<IBasicClass>();

            Random rnd = new Random(1);

            mockedErrorBasicClass.Setup(e => e.GetSomeNumber()).Returns(() => rnd.Next(10)); // returns 2, 1, 4...

            RetryPolicy<int> policy = Policy.HandleResult<int>(i => i % 2 != 1) // retry if the number is not odd.
                .RetryForever();
            BusinessLogic otherBusinessLogic = new BusinessLogic(policy, mockedErrorBasicClass.Object);

            //Act
            int num = otherBusinessLogic.CallSomeCodeThatNeedsToBeRetried();

            //Assert
            (num % 2).Should().Be(1);
        }

        [Fact]
        public void Should_Return_Odd_When_WaitAndRetry()
        {
            //Arrange 
            Mock<IBasicClass> mockedErrorBasicClass = new Mock<IBasicClass>();

            Random rnd = new Random(1);

            mockedErrorBasicClass.Setup(e => e.GetSomeNumber()).Returns(() => rnd.Next(10)); // returns 2, 1, 4...

            RetryPolicy<int> policy = Policy.HandleResult<int>(i => i % 2 != 1) // retry if the number is not odd.
                .WaitAndRetry(new[]
                {
                  TimeSpan.FromSeconds(1),
                  TimeSpan.FromSeconds(2),
                  TimeSpan.FromSeconds(3)
                });

            BusinessLogic otherBusinessLogic = new BusinessLogic(policy, mockedErrorBasicClass.Object);

            //Act
            int num = otherBusinessLogic.CallSomeCodeThatNeedsToBeRetried();

            //Assert
            (num % 2).Should().Be(1);
        }

        [Fact]
        public void Should_Return_Odd_When_WaitAndRetryForever()
        {
            //Arrange 
            Mock<IBasicClass> mockedErrorBasicClass = new Mock<IBasicClass>();

            Random rnd = new Random(1);

            mockedErrorBasicClass.Setup(e => e.GetSomeNumber()).Returns(() => rnd.Next(10)); // returns 2, 1, 4...

            RetryPolicy<int> policy = Policy.HandleResult<int>(i => i % 2 != 1) // retry if the number is not odd.
                .WaitAndRetryForever(retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            BusinessLogic otherBusinessLogic = new BusinessLogic(policy, mockedErrorBasicClass.Object);

            //Act
            int num = otherBusinessLogic.CallSomeCodeThatNeedsToBeRetried();

            //Assert
            (num % 2).Should().Be(1);
        }

        #endregion

        #region CircuitBreaker
        [Fact]
        public void Should_Return_Exception_When_Execute_CircuitBreaker()
        {
            BasicClass basicClass = new BasicClass();
            //Arrange 
            CircuitBreakerPolicy policy = Policy.Handle<Exception>()
            .CircuitBreaker(1, TimeSpan.FromSeconds(5),
            (ex, t) =>
            {
                Console.WriteLine("Circuit broken!");
            },
            () =>
            {
                Console.WriteLine("Circuit Reset!");
            });

            BusinessLogic businessLogic = new BusinessLogic(policy, basicClass);

            //Act
            businessLogic.GetHelloMessage();

            //Assert
            policy.CircuitState.Should().Be(CircuitState.Open);

            Thread.Sleep(TimeSpan.FromSeconds(6));
            policy.CircuitState.Should().Be(CircuitState.HalfOpen);
        }

        #endregion

        #region Timeout
        [Fact]
        public void Should_Return_Exception_When_Execute_Timeout()
        {
            //Arrange
            var timeoutPolicy = Policy.TimeoutAsync(2);
            Mock<IBasicClass> mockedErrorBasicClass = new Mock<IBasicClass>();

            //Act
            // This way dont respect the timeoutAsync, waiting for response ins 30 seconds, not in 2 seconds.
            //mockedErrorBasicClass.Setup(e => e.Func()).Returns(() => { Thread.Sleep(TimeSpan.FromSeconds(30)); return Task.CompletedTask; });

            mockedErrorBasicClass.Setup(e => e.Func()).Returns(async () => await Task.Delay(TimeSpan.FromSeconds(30)));
            //Assert
            timeoutPolicy.Awaiting(p => p.ExecuteAsync(() => mockedErrorBasicClass.Object.Func())).Should().ThrowAsync<TimeoutRejectedException>();

            //Act
            mockedErrorBasicClass.Setup(e => e.Func()).Returns(async () => await Task.Delay(TimeSpan.FromSeconds(0.1)));
            //Assert
            timeoutPolicy.Awaiting(p => p.ExecuteAsync(() => mockedErrorBasicClass.Object.Func())).Should().NotThrowAsync<TimeoutRejectedException>();
        }

        #endregion

        #region Bulkhead isolation
        [Fact]
        public void Should_Return_BulkheadRejectedException_When_Execute_Bulkhead_Four_times()
        {
            //Arrange 
            Mock<IBasicClass> mockedErrorBasicClass = new Mock<IBasicClass>();
            BulkheadPolicy policy = Policy.Bulkhead(3, 6);

            //Act
            mockedErrorBasicClass.Setup(e => e.Func()).Returns(async () => await Task.Delay(TimeSpan.FromSeconds(30)));

            BusinessLogic businessLogic = new BusinessLogic(policy, mockedErrorBasicClass.Object);

            //Assert
            policy.Awaiting(p =>
            {
                p.Execute(() => mockedErrorBasicClass.Object.Func());
                p.Execute(() => mockedErrorBasicClass.Object.Func());
                p.Execute(() => mockedErrorBasicClass.Object.Func());
                return p.Execute(() => mockedErrorBasicClass.Object.Func());
            }).Should().ThrowAsync<BulkheadRejectedException>();
        }

        #endregion

        #region Rate-limit
        [Fact]
        public void Should_Return_RateLimitRejectedException_When_Execute_RateLimit_Four_times()
        {
            // Allow up to 20 executions per second with a burst of 10 executions,
            // with a delegate to return the retry-after value to use if the rate
            // limit is exceeded.
            //Policy.RateLimit(20, TimeSpan.FromSeconds(1), 10, (retryAfter, context) =>
            //{
            //    return retryAfter.Add(TimeSpan.FromSeconds(2));
            //});

            //Arrange 
            Mock<IBasicClass> mockedErrorBasicClass = new Mock<IBasicClass>();

            RateLimitPolicy policy = Policy.RateLimit(3, TimeSpan.FromSeconds(1));

            //Act
            mockedErrorBasicClass.Setup(e => e.Func()).Returns(async () => await Task.Delay(TimeSpan.FromSeconds(30)));

            BusinessLogic businessLogic = new BusinessLogic(policy, mockedErrorBasicClass.Object);

            //Assert
            policy.Awaiting(p =>
            {
                p.Execute(() => mockedErrorBasicClass.Object.Func());
                p.Execute(() => mockedErrorBasicClass.Object.Func());
                p.Execute(() => mockedErrorBasicClass.Object.Func());
                return p.Execute(() => mockedErrorBasicClass.Object.Func());
            }).Should().ThrowAsync<RateLimitRejectedException>();
        }

        #endregion

        #region Cache
        [Fact]
        public void Should_Return_Cache_Policy_When_Execute_Twice()
        {
            //Arrange 
            var basicClass = new BasicClass();

            IMemoryCache memoryCache = new MemoryCache(new MemoryCacheOptions());
            MemoryCacheProvider memoryCacheProvider = new MemoryCacheProvider(memoryCache);
            CachePolicy policy = Policy.Cache(memoryCacheProvider, TimeSpan.FromMinutes(1));

            BusinessLogic businessLogic = new BusinessLogic(policy, basicClass);

            //Act
            int num = businessLogic.CallSomeCachedCode();
            int num2 = businessLogic.CallSomeCachedCode();

            //Assert
            (num2).Should().Be(num);
        }

        #endregion

        #region Fallback
        [Fact]
        public void Should_Return_Fallback_Policy_When_Multiple_Timeouts()
        {
            //Arrange 
            Mock<IBasicClass> mockedErrorBasicClass = new Mock<IBasicClass>();

            FallbackPolicy<Account> policy = Policy<Account>.Handle<Exception>().Fallback(() => Account.GetRandomAccount());

            //Act
            mockedErrorBasicClass.Setup(e => e.CreateNewAccount(It.IsAny<string>(), It.IsAny<int>()))
                .Throws(new TimeoutRejectedException("Mocked Timeout Exception"));

            BusinessLogic businessLogic = new BusinessLogic(policy, mockedErrorBasicClass.Object);

            var newAccount = businessLogic.CreateNewAccount("Teste", 10);

            //Assert
            newAccount.Should().NotBeNull();
            newAccount.Name.Should().Be("Carlos");
            newAccount.Age.Should().Be(31);
        }
        #endregion

        #region PolicyWrap
        [Fact]
        public void Should_Return_Fallback_PolicyWrap_When_Multiple_Timeouts()
        {
            //Arrange 
            FallbackPolicy<Account> fallbackPolicy = Policy<Account>.Handle<Exception>().Fallback(() => Account.GetRandomAccount());
            RetryPolicy retryPolicy = Policy.Handle<Exception>().WaitAndRetry(new[]
                {
                  TimeSpan.FromSeconds(1),
                  TimeSpan.FromSeconds(2),
                  TimeSpan.FromSeconds(3)
                });

            var policyWrap = fallbackPolicy.Wrap(retryPolicy);

            Mock<IBasicClass> mockedErrorBasicClass = new Mock<IBasicClass>();

            //Act
            mockedErrorBasicClass.Setup(e => e.CreateNewAccount(It.IsAny<string>(), It.IsAny<int>()))
                .Throws(new TimeoutRejectedException("Mocked Timeout Exception"));

            BusinessLogic businessLogic = new BusinessLogic(policyWrap, mockedErrorBasicClass.Object);

            var newAccount = businessLogic.CreateNewAccountWithPolicyWrap("Teste", 10);

            //Assert
            newAccount.Should().NotBeNull();
            newAccount.Name.Should().Be("Carlos");
            newAccount.Age.Should().Be(31);
        }
        #endregion

        #region NoOp
        [Fact]
        public void Should_Return_Even_With_NoOp()
        {
            //Arrange 
            Mock<IBasicClass> mockedErrorBasicClass = new Mock<IBasicClass>();

            Random rnd = new Random(1); // rnd.Next(10) retruns 2, 1, 4 

            mockedErrorBasicClass.Setup(e => e.GetSomeNumber()).Returns(() => rnd.Next(10));

            ISyncPolicy policy = Policy.NoOp();
            BusinessLogic otherBusinessLogic = new BusinessLogic(policy, mockedErrorBasicClass.Object);

            //Act
            int num = otherBusinessLogic.CallSomeSlowBadCode();

            //Assert
            (num % 2).Should().Be(0); //even number
        }
        #endregion
    }
}

