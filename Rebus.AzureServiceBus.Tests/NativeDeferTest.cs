﻿using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rebus.Activation;
using Rebus.AzureServiceBus.NameFormat;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Routing.TypeBased;
using Rebus.Tests.Contracts;
using Rebus.Tests.Contracts.Extensions;
using Rebus.Threading.TaskParallelLibrary;

#pragma warning disable 1998

namespace Rebus.AzureServiceBus.Tests
{
    [TestFixture]
    public class NativeDeferTest : FixtureBase
    {
        static readonly string QueueName = TestConfig.GetName("input");
        BuiltinHandlerActivator _activator;
        IBus _bus;
        IBusStarter _busStarter;

        protected override void SetUp()
        {
            var connectionString = AsbTestConfig.ConnectionString;
            var consoleLoggerFactory = new ConsoleLoggerFactory(false);
            var asyncTaskFactory = new TplAsyncTaskFactory(consoleLoggerFactory);
            
            using (var transport = new AzureServiceBusTransport(connectionString, QueueName, consoleLoggerFactory, asyncTaskFactory, new DefaultNameFormatter()))
            {
                transport.PurgeInputQueue();
            }

            _activator = new BuiltinHandlerActivator();

            _busStarter = Configure.With(_activator)
                .Transport(t => t.UseAzureServiceBus(connectionString, QueueName))
                .Routing(r => r.TypeBased().Map<TimedMessage>(QueueName))
                .Options(o =>
                {
                    o.LogPipeline();
                })
                .Create();

            _bus = _busStarter.Bus;

            Using(_bus);
        }

        [Test]
        public async Task UsesNativeDeferralMechanism()
        {
            var done = new ManualResetEvent(false);
            var receiveTime = DateTimeOffset.MinValue;
            var hadDeferredUntilHeader = false;

            _activator.Handle<TimedMessage>(async (bus, context, message) =>
            {
                receiveTime = DateTimeOffset.Now;

                hadDeferredUntilHeader = context.TransportMessage.Headers.ContainsKey(Headers.DeferredUntil);

                done.Set();
            });

            _busStarter.Start();

            var sendTime = DateTimeOffset.Now;

            await _bus.Defer(TimeSpan.FromSeconds(5), new TimedMessage { Time = sendTime });

            done.WaitOrDie(TimeSpan.FromSeconds(8), "Did not receive 5s-deferred message within 8 seconds of waiting....");

            var delay = receiveTime - sendTime;

            Console.WriteLine($"Message was delayed {delay}");

            Assert.That(delay, Is.GreaterThan(TimeSpan.FromSeconds(2.5)), "The message not delayed ~5 seconds as expected!");
            Assert.That(delay, Is.LessThan(TimeSpan.FromSeconds(8)), "The message not delayed ~5 seconds as expected!");

            Assert.That(hadDeferredUntilHeader, Is.False, "Received message still had the '{0}' header - we must remove that", Headers.DeferredUntil);
        }

        class TimedMessage
        {
            public DateTimeOffset Time { get; set; }
        }
    }
}