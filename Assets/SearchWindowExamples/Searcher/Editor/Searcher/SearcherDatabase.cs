using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityEditor.Searcher
{
    public class SearcherDatabase : SearcherDatabaseBase
    {
        private class Result
        {
            public SearcherItem item;
            public float maxScore;
        }

        private static bool s_IsParallel = true;

        public Func<string, SearcherItem, bool> matchFilter { get; set; }

        public static SearcherDatabase Create(
            List<SearcherItem> items,
            string databaseDirectory,
            bool serializeToFile = true
        )
        {
            if (serializeToFile && databaseDirectory != null && !Directory.Exists(databaseDirectory))
                Directory.CreateDirectory(databaseDirectory);

            var database = new SearcherDatabase(databaseDirectory, items);

            if (serializeToFile)
                database.SerializeToFile();

            return database;
        }

        public static SearcherDatabase Load(string databaseDirectory)
        {
            if (!Directory.Exists(databaseDirectory))
                throw new InvalidOperationException("databaseDirectory not found.");

            var database = new SearcherDatabase(databaseDirectory, null);
            database.LoadFromFile();

            return database;
        }

        private SearcherDatabase(string databaseDirectory, List<SearcherItem> db)
            : base(databaseDirectory)
        {
            m_ItemList = new List<SearcherItem>();
            int nextId = 0;

            if (db != null)
                foreach (var item in db)
                    AddItemToIndex(item, ref nextId, null);
        }

        public override List<SearcherItem> Search(string query, out float localMaxScore)
        {
            // Match assumes the query is trimmed
            query = query.Trim(' ', '\t');
            localMaxScore = 0;

            if (string.IsNullOrWhiteSpace(query))
            {
                if (matchFilter == null)
                    return m_ItemList;

                if (s_IsParallel && m_ItemList.Count > 100)
                    return FilterMultiThreaded(query);

                return FilterSingleThreaded(query);
            }

            List<SearcherItem> finalResults = new List<SearcherItem> { null };
            Result max = new Result();
            if (s_IsParallel && m_ItemList.Count > 100)
                SearchMultithreaded(query, max, finalResults);
            else
                SearchSingleThreaded(query, max, finalResults);

            localMaxScore = max.maxScore;
            if (max.item != null)
                finalResults[0] = max.item;
            else
                finalResults.RemoveAt(0);

            return finalResults;
        }

        public override SearcherItem GetItemFromId(int i)
        {
            return m_ItemList[i];
        }

        protected virtual bool Match(string query, SearcherItem item, out float score)
        {
            var filter = matchFilter?.Invoke(query, item) ?? true;
            return Match(query, item.path, out score) && filter;
        }

        private List<SearcherItem> FilterSingleThreaded(string query)
        {
            var result = new List<SearcherItem>();

            foreach (var searcherItem in m_ItemList)
            {
                if (!matchFilter.Invoke(query, searcherItem))
                    continue;

                result.Add(searcherItem);
            }

            return result;
        }

        private List<SearcherItem> FilterMultiThreaded(string query)
        {
            var result = new List<SearcherItem>();
            var count = Environment.ProcessorCount;
            Task[] tasks = new Task[count];
            List<SearcherItem>[] lists = new List<SearcherItem>[count];
            int itemsPerTask = (int)Math.Ceiling(m_ItemList.Count / (float)count);

            for (int i = 0; i < count; i++)
            {
                int i1 = i;
                tasks[i] = Task.Run(() =>
                {
                    lists[i1] = new List<SearcherItem>();

                    for (int j = 0; j < itemsPerTask; j++)
                    {
                        int index = j + itemsPerTask * i1;
                        if (index >= m_ItemList.Count)
                            break;

                        SearcherItem item = m_ItemList[index];
                        if (!matchFilter.Invoke(query, item))
                            continue;

                        lists[i1].Add(item);
                    }
                });
            }

            Task.WaitAll(tasks);

            for (int i = 0; i < count; i++)
            {
                result.AddRange(lists[i]);
            }

            return result;
        }

        private void SearchSingleThreaded(string query, Result max, List<SearcherItem> finalResults)
        {
            for (int i = 0; i < m_ItemList.Count; i++)
            {
                var item = m_ItemList[i];
                float score = 0;
                if (query.Length == 0 || Match(query, item, out score))
                {
                    if (score > max.maxScore)
                    {
                        if (max.item != null)
                            finalResults.Add(max.item);
                        max.item = item;
                        max.maxScore = score;
                    }
                    else
                        finalResults.Add(item);
                }
            }
        }

        private void SearchMultithreaded(string query, Result max, List<SearcherItem> finalResults)
        {
            var count = Environment.ProcessorCount;
            Task[] tasks = new Task[count];
            Result[] localResults = new Result[count];
            ConcurrentQueue<SearcherItem> queue = new ConcurrentQueue<SearcherItem>();
            int itemsPerTask = (int)Math.Ceiling(m_ItemList.Count / (float)count);

            for (int i = 0; i < count; i++)
            {
                int i1 = i;
                localResults[i1] = new Result();
                tasks[i] = Task.Run(() =>
                {
                    var result = localResults[i1];
                    for (int j = 0; j < itemsPerTask; j++)
                    {
                        int index = j + itemsPerTask * i1;
                        if (index >= m_ItemList.Count)
                            break;
                        SearcherItem item = m_ItemList[index];
                        float score = 0;
                        if (query.Length == 0 || Match(query, item, out score))
                        {
                            if (score > result.maxScore)
                            {
                                if (result.item != null)
                                    queue.Enqueue(result.item);
                                result.maxScore = score;
                                result.item = item;
                            }
                            else
                                queue.Enqueue(item);
                        }
                    }
                });
            }

            Task.WaitAll(tasks);

            for (int i = 0; i < count; i++)
            {
                if (localResults[i].maxScore > max.maxScore)
                {
                    max.maxScore = localResults[i].maxScore;
                    if (max.item != null)
                        queue.Enqueue(max.item);
                    max.item = localResults[i].item;
                }
                else if (localResults[i].item != null)
                    queue.Enqueue(localResults[i].item);
            }

            finalResults.AddRange(queue.OrderBy(i => i.id));
        }

        private static int NextSeparator(string s, int index)
        {
            for (; index < s.Length; index++)
                if (IsWhiteSpace(s[index])) // || char.IsUpper(s[index]))
                    return index;
            return -1;
        }

        private static bool IsWhiteSpace(char c)
        {
            return c == ' ' || c == '\t';
        }

        private static char ToLowerAsciiInvariant(char c)
        {
            if ('A' <= c && c <= 'Z')
                c |= ' ';
            return c;
        }

        private static bool StartsWith(string s, int sStart, int sCount, string prefix, int prefixStart, int prefixCount)
        {
            if (prefixCount > sCount)
                return false;
            for (int i = 0; i < prefixCount; i++)
            {
                if (ToLowerAsciiInvariant(s[sStart + i]) != ToLowerAsciiInvariant(prefix[prefixStart + i]))
                    return false;
            }

            return true;
        }

        private static bool Match(string query, string itemPath, out float score)
        {
            int queryPartStart = 0, pathPartStart = 0;

            score = 0;
            int skipped = 0;
            do
            {
                // skip remaining spaces in path
                while (pathPartStart < itemPath.Length && IsWhiteSpace(itemPath[pathPartStart]))
                    pathPartStart++;

                // query is not done, nothing remaining in path, failure
                if (pathPartStart > itemPath.Length - 1)
                {
                    score = 0;
                    return false;
                }

                // skip query spaces. notice the + 1
                while (queryPartStart < query.Length && IsWhiteSpace(query[queryPartStart]))
                    queryPartStart++;

                // find next separator in query
                int queryPartEnd = query.IndexOf(' ', queryPartStart);
                if (queryPartEnd == -1)
                    queryPartEnd = query.Length; // no spaces, take everything remaining

                // next space, starting after the path part last char
                int pathPartEnd = NextSeparator(itemPath, pathPartStart + 1);
                if (pathPartEnd == -1)
                    pathPartEnd = itemPath.Length;


                int queryPartLength = queryPartEnd - queryPartStart;
                int pathPartLength = pathPartEnd - pathPartStart;
                bool match = StartsWith(itemPath, pathPartStart, pathPartLength,
                    query, queryPartStart, queryPartLength);

                pathPartStart = pathPartEnd;

                if (!match)
                {
                    skipped++;
                    continue;
                }

                score += queryPartLength / (float)(Mathf.Max(1, pathPartLength));
                if (queryPartEnd == query.Length)
                {
                    int pathPartCount = 1;
                    while (-1 != pathPartStart)
                    {
                        pathPartStart = NextSeparator(itemPath, pathPartStart + 1);
                        pathPartCount++;
                    }

                    int queryPartCount = 1;
                    while (-1 != queryPartStart)
                    {
                        queryPartStart = NextSeparator(query, queryPartStart + 1);
                        pathPartCount++;
                    }

                    score *= queryPartCount / (float)pathPartCount;
                    score *= 1 / (1.0f + skipped);

                    return true; // successfully matched all query parts
                }

                queryPartStart = queryPartEnd + 1;
            } while (true);
        }
    }
}
