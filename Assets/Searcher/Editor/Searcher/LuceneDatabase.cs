using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Store;
using Lucene.Net.Index;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Search;
using Lucene.Net.QueryParsers;
using UnityEngine;
using System;

namespace UnityEditor.Searcher
{
    [Serializable]
    public class LuceneDatabase : SearcherDatabaseBase
    {
        public IList<SearcherItem> itemList
        {
            get { return m_ItemList; }
        }

        public override SearcherItem GetItemFromId(int i)
        {
            return itemList[i];
        }

        private static readonly string[] s_AnalyzedFields =
            new string[] { "name", "help", "parents" };

        private const string k_MainDirectoryName = "/MainIndex";
        private const string k_AutoCompleteDirectorName = "/AutoCompleteIndex";

        internal Directory mainDirectory { get; private set; }
        internal Directory autoCompleteDirectory { get; private set; }

        internal Analyzer analyser { get; private set; }

        [SerializeField]
        private List<string> m_AnalyzedFields;

        public IEnumerable<string> analyzedFields
        {
            get { return m_AnalyzedFields; }
        }

        public static LuceneDatabase Create(
            IList<SearcherItem> items,
            string databaseDirectory,
            IEnumerable<string> analyzedFields = null
        )
        {
            if (!System.IO.Directory.Exists(databaseDirectory))
                System.IO.Directory.CreateDirectory(databaseDirectory);

            var database = new LuceneDatabase(databaseDirectory, analyzedFields);
            database.Fill(items);
            database.SerializeToFile();

            return database;
        }

        public static LuceneDatabase Load(string databaseDirectory)
        {
            if (!System.IO.Directory.Exists(databaseDirectory))
                throw new InvalidOperationException("databaseDirectory not found.");

            var database = new LuceneDatabase(databaseDirectory, null);
            database.LoadFromFile();

            return database;
        }

        private LuceneDatabase(string databaseDirectory, IEnumerable<string> analyzedFields)
            : base(databaseDirectory)
        {
            m_AnalyzedFields = analyzedFields?.ToList() ?? s_AnalyzedFields.ToList();

            mainDirectory = FSDirectory.Open(databaseDirectory + k_MainDirectoryName);
            autoCompleteDirectory = FSDirectory.Open(databaseDirectory + k_AutoCompleteDirectorName);

            m_ItemList = new List<SearcherItem>();
            analyser = new SimpleAnalyzer();
        }

        private void Fill(IList<SearcherItem> items)
        {
            CreateMainIndex(items);
        }

        private void CreateMainIndex(IList<SearcherItem> items)
        {
            using (var indexWriter = new IndexWriter(mainDirectory, analyser, true, new KeepOnlyLastCommitDeletionPolicy(), IndexWriter.MaxFieldLength.UNLIMITED))
            {
                int lastId = 0;
                foreach (SearcherItem item in items)
                {
                    AddItemToIndex(item, ref lastId, i => indexWriter.AddDocument(i.GetDocument()));
                }

                indexWriter.Commit();
            }
        }

        public override List<SearcherItem> Search(string query, out float maxWeight)
        {
            return ExecuteQuery(query, out maxWeight).Select(ToData).Where(data => data != null).ToList();
        }

        private SearcherItem ToData(Document document)
        {
            var id = document.GetField(SearcherItem.k_IdField);
            if (id == null)
                return null;

            var uid = int.Parse(id.StringValue);
            return m_ItemList[uid];
        }

        private IList<Document> ExecuteQuery(string query, out float maxScore, int maxResultCount = 10000)
        {
            var results = new List<Document>();

            using (var indexSearcher = new IndexSearcher(mainDirectory))
            {
                // Escape special characters in the query.

                Query finalQuery;

                if (string.IsNullOrEmpty(query))
                {
                    finalQuery = new MatchAllDocsQuery();
                }
                else
                {
                    finalQuery = new BooleanQuery();

                    query = QueryParser.Escape(query);

                    // resulting query for "variable constant th":
                    // parents:"variable constant th"^3.0 +parents:"variable constant"^2.0 name:th* 
                    // the first part is optionnal - try to match the full path. won't work while typing
                    // the second part will exclude the last word and only search for "full" words
                    // (ie. "variable constant", not "th")
                    // the last part matches the last word as a prefix ("th*")

                    string[] split = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var exactFullPathQuery = new MultiPhraseQuery { Boost = 3 };
                    var exactFullPathQueryNoLastWord = new MultiPhraseQuery { Boost = 2 };

                    for (int index = 0; index < split.Length; index++)
                    {
                        var term = new Term("parents", split[index]);
                        if (index < split.Length - 1)
                            exactFullPathQueryNoLastWord.Add(term);
                        exactFullPathQuery.Add(term);
                    }

                    if (exactFullPathQueryNoLastWord.GetTermArrays().Count > 0)
                        ((BooleanQuery)finalQuery).Add(exactFullPathQueryNoLastWord, Occur.MUST);
                    ((BooleanQuery)finalQuery).Add(exactFullPathQuery, Occur.SHOULD);

                    var nameQuery = query.Substring(query.LastIndexOf(' ') + 1);
                    if (nameQuery.Length > 0) // "variable " should not search for "name: "
                        ((BooleanQuery)finalQuery).Add(new BooleanClause(new PrefixQuery(new Term("name", nameQuery)), Occur.SHOULD));
                }

                Weight w = finalQuery.Weight(indexSearcher);
                TopDocs searchResults = indexSearcher.Search(w, null, maxResultCount);
                maxScore = 0.0f;
                foreach (ScoreDoc scoreDocument in searchResults.ScoreDocs)
                {
                    if (scoreDocument.Score > maxScore)
                        maxScore = scoreDocument.Score;

                    int documentHitNumber = scoreDocument.Doc;
                    Document hitDocument = indexSearcher.Doc(documentHitNumber);
                    results.Add(hitDocument);
                }
            }

            return results;
        }
    }
}
