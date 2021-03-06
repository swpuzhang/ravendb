using System.Linq;
using FastTests;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Indexes;
using Raven.Tests.Core.Utils.Entities;
using Xunit;

namespace SlowTests.Tests.Indexes
{
    public class CreateIndexesWithCasting : RavenTestBase
    {
        [Fact]
        public void WillPreserverTheCasts()
        {
            var indexDefinition = new WithCasting
            {
#pragma warning disable CS0618 // Type or member is obsolete
                Conventions = new DocumentConventions { PrettifyGeneratedLinqExpressions = false }
#pragma warning restore CS0618 // Type or member is obsolete
            }.CreateIndexDefinition();

            var map = indexDefinition.Maps.First();

            Assert.Contains("docs.People.Select(person => new {", map);
            Assert.Contains("Id = ((long) person.Name.Length)", map);
        }

        public class WithCasting : AbstractIndexCreationTask<Person>
        {
            public WithCasting()
            {
                Map = persons => from person in persons
                                 select new { Id = (long)person.Name.Length };
            }
        }
    }
}
