using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework.Constraints;

namespace UnityEditor.Searcher.Tests
{
    internal class Is : NUnit.Framework.Is
    {
        public static SearcherItemCollectionEquivalentConstraint SearcherItemCollectionEquivalent(IEnumerable<SearcherItem> expected)
        {
            return new SearcherItemCollectionEquivalentConstraint(expected);
        }
    }

    public class SearcherItemCollectionEquivalentConstraint : CollectionItemsEqualConstraint
    {
        private readonly List<SearcherItem> expected;

        public SearcherItemCollectionEquivalentConstraint(IEnumerable<SearcherItem> expected)
            : base(expected)
        {
            this.expected = expected.ToList();
        }

        protected override bool Matches(IEnumerable actual)
        {
            if (expected == null)
            {
                Description = "Expected is not a valid collection";
                return false;
            }

            if (!(actual is IEnumerable<SearcherItem> actualCollection))
            {
                Description = "Actual is not a valid collection";
                return false;
            }

            var actualList = actualCollection.ToList();
            if (actualList.Count != expected.Count)
            {
                Description = $"Collections lengths are not equal. \nExpected length: {expected.Count}, " +
                    $"\nBut was: {actualList.Count}";
                return false;
            }

            for (var i = 0; i < expected.Count; ++i)
            {
                var res1 = expected[i].name;
                var res2 = actualList[i].name;
                if (!string.Equals(res1, res2))
                {
                    Description = $"Object at index {i} are not the same.\nExpected: {res1},\nBut was: {res2}";
                    return false;
                }

                var constraint = new SearcherItemCollectionEquivalentConstraint(expected[i].children);
                if (constraint.Matches(actualList[i].children))
                    continue;

                Description = constraint.Description;
                return false;
            }

            return true;
        }
    }
}
