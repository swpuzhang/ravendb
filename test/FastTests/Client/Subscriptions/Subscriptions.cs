﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents.Exceptions.Subscriptions;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Subscriptions;
using Xunit;
using Sparrow;

namespace FastTests.Client.Subscriptions
{
    public class Subscriptions : SubscriptionTestBase
    {
        [Fact]
        public async Task CreateSubscription()
        {
            using (var store = GetDocumentStore())
            {
                var subscriptionCreationParams = new SubscriptionCreationOptions
                {
                    Criteria = new SubscriptionCriteria("People")
                };  
                var subsId = await store.AsyncSubscriptions.CreateAsync(subscriptionCreationParams);

                var subscriptionsConfig = await store.AsyncSubscriptions.GetSubscriptionsAsync(0, 10);

                Assert.Equal(1, subscriptionsConfig.Count);
                Assert.Equal(subscriptionCreationParams.Criteria.Collection, subscriptionsConfig[0].Criteria.Collection);
                Assert.Equal(subscriptionCreationParams.Criteria.Script, subscriptionsConfig[0].Criteria.Script);
                Assert.Equal(0, subscriptionsConfig[0].ChangeVector.Length);
                Assert.Equal(subsId, subscriptionsConfig[0].SubscriptionId);
            }
        }

        [Fact]
        public async Task BasicSusbscriptionTest()
        {
            using (var store = GetDocumentStore())
            {
                await CreateDocuments(store, 1);

                var lastChangeVector = (await store.Admin.SendAsync(new GetStatisticsOperation())).DatabaseChangeVector;
                await CreateDocuments(store, 5);

                var subscriptionCreationParams = new SubscriptionCreationOptions()
                {
                    Criteria = new SubscriptionCriteria("Things"),
                    ChangeVector = lastChangeVector
                };
                var subsId = await store.AsyncSubscriptions.CreateAsync(subscriptionCreationParams);
                using (var subscription = store.AsyncSubscriptions.Open<Thing>(new SubscriptionConnectionOptions(subsId)))
                {
                    var list = new BlockingCollection<Thing>();
                    GC.KeepAlive(subscription.Run(u =>
                    {
                        foreach (var item in u.Items)
                        {
                            list.Add(item.Result);
                        }
                    }));
                    Thing thing;
                    for (var i = 0; i < 5; i++)
                    {
                        Assert.True(list.TryTake(out thing, 1000));
                    }
                    Assert.False(list.TryTake(out thing, 50));
                }
            }
        }

        [Fact]
        public async Task SubscriptionStrategyConnectIfFree()
        {
            using (var store = GetDocumentStore())
            {
                await CreateDocuments(store, 1);

                var lastChangeVector = (await store.Admin.SendAsync(new GetStatisticsOperation())).DatabaseChangeVector ?? null;
                await CreateDocuments(store, 5);

                var subscriptionCreationParams = new SubscriptionCreationOptions()
                {
                    Criteria = new SubscriptionCriteria("Things"),
                    ChangeVector = lastChangeVector
                };
                var subsId = await store.AsyncSubscriptions.CreateAsync(subscriptionCreationParams);
                using (
                    var acceptedSubscription = store.AsyncSubscriptions.Open<Thing>(new SubscriptionConnectionOptions(subsId)
                    {
                        TimeToWaitBeforeConnectionRetry = TimeSpan.FromSeconds(20)
                    }))
                {
                    var acceptedSubscriptionList = new BlockingCollection<Thing>();

                    GC.KeepAlive(acceptedSubscription.Run(u =>
                    {
                        foreach (var item in u.Items)
                        {
                            acceptedSubscriptionList.Add(item.Result);
                        }
                    }));


                    Thing thing;

                    // wait until we know that connection was established
                    for (var i = 0; i < 5; i++)
                    {
                        Assert.True(acceptedSubscriptionList.TryTake(out thing, 1000));
                    }

                    Assert.False(acceptedSubscriptionList.TryTake(out thing, 50));
                    // open second subscription
                    using (var rejectedSubscription =
                        store.AsyncSubscriptions.Open<Thing>(new SubscriptionConnectionOptions(subsId)
                        {
                            Strategy = SubscriptionOpeningStrategy.OpenIfFree,
                            TimeToWaitBeforeConnectionRetry = TimeSpan.FromMilliseconds(2000)
                        }))
                    {
                        // sometime not throwing (on linux) when written like this:
                        // await Assert.ThrowsAsync<SubscriptionInUseException>(async () => await rejectedSubscription.StartAsync());
                        // so we put this in a try block
                        try
                        {
                            await rejectedSubscription.Run(_ => { });
                            Assert.False(true, "Exepcted a throw here");
                        }
                        catch (SubscriptionInUseException)
                        {
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task SubscriptionWaitStrategy()
        {
            using (var store = GetDocumentStore())
            {
                await CreateDocuments(store, 1);

                var lastChangeVector = (await store.Admin.SendAsync(new GetStatisticsOperation())).DatabaseChangeVector;

                var subscriptionCreationParams = new SubscriptionCreationOptions()
                {
                    Criteria = new SubscriptionCriteria("Things"),
                    ChangeVector = lastChangeVector
                };

                await CreateDocuments(store, 5);
                var subsId = await store.AsyncSubscriptions.CreateAsync(subscriptionCreationParams);
                using (
                    var acceptedSubscription = store.AsyncSubscriptions.Open<Thing>(new SubscriptionConnectionOptions(subsId)))
                {

                    var acceptedSusbscriptionList = new BlockingCollection<Thing>();
                    var waitingSubscriptionList = new BlockingCollection<Thing>();

                    var ackSentAmre = new AsyncManualResetEvent();
                    acceptedSubscription.AfterAcknowledgment += b => { ackSentAmre.Set(); return Task.CompletedTask; };


                    GC.KeepAlive(acceptedSubscription.Run(x=>
                    {
                        foreach (var item in x.Items)
                        {
                            acceptedSusbscriptionList.Add(item.Result);
                        }
                        Thread.Sleep(20);
                    }));

                    // wait until we know that connection was established

                    Thing thing;
                    // wait until we know that connection was established
                    for (var i = 0; i < 5; i++)
                    {
                        Assert.True(acceptedSusbscriptionList.TryTake(out thing, 50000));
                    }

                    Assert.False(acceptedSusbscriptionList.TryTake(out thing, 50));

                    // open second subscription
                    using (
                        var waitingSubscription =
                            store.AsyncSubscriptions.Open<Thing>(new SubscriptionConnectionOptions(subsId)
                            {
                                Strategy = SubscriptionOpeningStrategy.WaitForFree,
                                TimeToWaitBeforeConnectionRetry = TimeSpan.FromMilliseconds(250)
                            }))
                    {

                        GC.KeepAlive(waitingSubscription.Run(x =>
                        {
                            foreach (var item in x.Items)
                            {
                                waitingSubscriptionList.Add(item.Result);
                            }
                        }));

                        Assert.True(await ackSentAmre.WaitAsync(TimeSpan.FromSeconds(50)));

                        acceptedSubscription.Dispose();

                        await CreateDocuments(store, 5);

                        // wait until we know that connection was established
                        for (var i = 0; i < 5; i++)
                        {
                            Assert.True(waitingSubscriptionList.TryTake(out thing, 3000));
                        }

                        Assert.False(waitingSubscriptionList.TryTake(out thing, 50));
                    }
                }
            }
        }

        [Fact]
        public async Task SubscriptionSimpleTakeOverStrategy()
        {
            using (var store = GetDocumentStore())
            {
                await CreateDocuments(store, 1);

                var lastChangeVector = (await store.Admin.SendAsync(new GetStatisticsOperation())).DatabaseChangeVector ?? null;
                await CreateDocuments(store, 5);

                var subscriptionCreationParams = new SubscriptionCreationOptions()
                {
                    Criteria = new SubscriptionCriteria("Things"),
                    ChangeVector = lastChangeVector
                };

                var subsId = await store.AsyncSubscriptions.CreateAsync(subscriptionCreationParams);

                using (
                    var acceptedSubscription = store.AsyncSubscriptions.Open<Thing>(new SubscriptionConnectionOptions(subsId)))
                {
                    var acceptedSusbscriptionList = new BlockingCollection<Thing>();
                    var takingOverSubscriptionList = new BlockingCollection<Thing>();
                    long counter = 0;
                    
                    var batchProccessedByFirstSubscription = new AsyncManualResetEvent();

                    acceptedSubscription.AfterAcknowledgment +=
                        b =>
                        {
                            if (Interlocked.Read(ref counter) == 5)
                                batchProccessedByFirstSubscription.Set();
                            return Task.CompletedTask;
                        };

                    GC.KeepAlive(acceptedSubscription.Run(x =>
                    {
                        foreach (var item in x.Items)
                        {
                            Interlocked.Increment(ref counter);
                            acceptedSusbscriptionList.Add(item.Result);
                        }
                    }));


                    Thing thing;

                    // wait until we know that connection was established
                    for (var i = 0; i < 5; i++)
                    {
                        Assert.True(acceptedSusbscriptionList.TryTake(out thing, 5000), "no doc");
                    }

                    Assert.True(await batchProccessedByFirstSubscription.WaitAsync(TimeSpan.FromSeconds(15)), "no ack");

                    Assert.False(acceptedSusbscriptionList.TryTake(out thing));

                    // open second subscription
                    using (var takingOverSubscription = store.AsyncSubscriptions.Open<Thing>(
                        new SubscriptionConnectionOptions(subsId)
                    {
                        
                        Strategy = SubscriptionOpeningStrategy.TakeOver
                    }))
                    {
                        GC.KeepAlive(takingOverSubscription.Run(x =>
                        {
                            foreach (var item in x.Items)
                            {
                                takingOverSubscriptionList.Add(item.Result);
                            }
                        }));

                        await CreateDocuments(store, 5);

                        // wait until we know that connection was established
                        for (var i = 0; i < 5; i++)
                        {
                            Assert.True(takingOverSubscriptionList.TryTake(out thing, 5000), "no doc takeover");
                        }
                        Assert.False(takingOverSubscriptionList.TryTake(out thing));
                    }
                }
            }
        }
    }
}