﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac.Core;
using Xunit;

namespace Autofac.Test
{
    public class ConcurrentTests
    {
        [Fact]
        public void WhenTwoThreadsInitialiseASharedInstanceSimultaneouslyViaChildLifetime_OnlyOneInstanceIsActivated()
        {
            int activationCount = 0;
            var results = new ConcurrentBag<object>();
            var exceptions = new ConcurrentBag<Exception>();

            var builder = new ContainerBuilder();
            builder.Register(c =>
                {
                    Interlocked.Increment(ref activationCount);
                    Thread.Sleep(500);
                    return new object();
                })
                .SingleInstance();

            var container = builder.Build();

            ThreadStart work = () =>
            {
                 try
                 {
                     var o = container.BeginLifetimeScope().Resolve<object>();
                     results.Add(o);
                 }
                 catch (Exception ex)
                 {
                     exceptions.Add(ex);
                 }
            };

            var t1 = new Thread(work);
            var t2 = new Thread(work);
            t1.Start();
            t2.Start();
            t1.Join();
            t2.Join();

            Assert.Equal(1, activationCount);
            Assert.Empty(exceptions);
            Assert.Equal(1, results.Distinct().Count());
        }

#if !NETCOREAPP1_0
        [Fact]
        public void ConcurrentResolveOperationsForNonSharedInstancesFromDifferentLifetimes_DoNotBlock()
        {
            var evt = new ManualResetEvent(false);

            var builder = new ContainerBuilder();
            builder.Register((c, p) =>
                {
                    if (p.TypedAs<bool>())
                        evt.WaitOne();
                    return new object();
                });

            var container = builder.Build();

            var unblocked = 0;
            var blockedScope = container.BeginLifetimeScope();
            var blockedThread = new Thread(() =>
            {
                blockedScope.Resolve<object>(TypedParameter.From(true));
                Interlocked.Increment(ref unblocked);
            });
            blockedThread.Start();
            Thread.Sleep(500);

            container.Resolve<object>(TypedParameter.From(false));
            container.BeginLifetimeScope().Resolve<object>(TypedParameter.From(false));

            Thread.MemoryBarrier();
            Assert.Equal(0, unblocked);
            evt.Set();
            blockedThread.Join();
        }
#endif

        [Fact]
        public void ConcurrentResolveOperationsFromDifferentContainers_DoesNotThrow()
        {
            var task1 = Task.Factory.StartNew(ResolveObjectInstanceLoop);
            var task2 = Task.Factory.StartNew(ResolveObjectInstanceLoop);
            Task.WaitAll(task1, task2);
        }

        private static void ResolveObjectInstanceLoop()
        {
            var builder = new ContainerBuilder();
            builder.RegisterType<object>();
            var container = builder.Build();

            for (var index = 0; index < 100; index++)
            {
                container.Resolve<object>();
            }
        }

#if !NETCOREAPP1_0 && !NET451
        [Fact]
        public void NoLockWhenResolvingExistingSingleInstance()
        {
            var builder = new ContainerBuilder();
            Func<IContainer> containerProvider = default(Func<IContainer>);
            builder.Register(c => default(Int32)).SingleInstance();
            builder.Register(c =>
            {
                using (var mres = new ManualResetEventSlim())
                {
                    ThreadPool.QueueUserWorkItem(state =>
                    {
                        containerProvider().Resolve<Int32>();
                        mres.Set();
                    });
                    mres.Wait(1250);
                }
                return new object();
            });

            IContainer container = builder.Build();
            containerProvider = () => container;
            container.Resolve<Int32>();
            container.Resolve<object>();
        }
#endif
    }
}
