﻿namespace Raven.Tests.Issues
{
	using System.Threading;

	using Raven.Bundles.Tests.Replication;
	using Raven.Client.Exceptions;

	using Xunit;

	public class RavenDB_578 : ReplicationBase
	{
		public class Person
		{
			public string FirstName { get; set; }

			public string LastName { get; set; }

			public string MiddleName { get; set; }
		}

		[Fact]
		public void DeletingConflictedDocumentOnOneServerShouldCauseConflictOnSecondOne()
		{
			var store1 = CreateStore();
			var store2 = CreateStore();
			using (var session = store1.OpenSession())
			{
				session.Store(new Person { FirstName = "John" });
				session.SaveChanges();
			}

			using (var session = store2.OpenSession())
			{
				session.Store(new Person { FirstName = "Doe" });
				session.SaveChanges();
			}

			TellFirstInstanceToReplicateToSecondInstance();

			var conflictException = Assert.Throws<ConflictException>(() =>
			{
				for (int i = 0; i < RetriesCount; i++)
				{
					using (var session = store2.OpenSession())
					{
						session.Load<Person>("people/1");
						Thread.Sleep(100);
					}
				}
			});

			Assert.Equal("Conflict detected on people/1, conflict must be resolved before the document will be accessible", conflictException.Message);

			TellSecondInstanceToReplicateToFirstInstance();

			store2.DatabaseCommands.Delete("people/1", null);

			conflictException = Assert.Throws<ConflictException>(() =>
			{
				for (int i = 0; i < RetriesCount; i++)
				{
					using (var session = store1.OpenSession())
					{
						session.Load<Person>("people/1");
						Thread.Sleep(100);
					}
				}
			});

			Assert.Equal("Conflict detected on people/1, conflict must be resolved before the document will be accessible", conflictException.Message);

			try
			{
				store1.DatabaseCommands.Get("people/1");
			}
			catch (ConflictException e)
			{
				var c1 = store1.DatabaseCommands.Get(e.ConflictedVersionIds[0]);
				var c2 = store1.DatabaseCommands.Get(e.ConflictedVersionIds[1]);

				Assert.NotNull(c1);
				Assert.Null(c2);
			}
		}
	}
}