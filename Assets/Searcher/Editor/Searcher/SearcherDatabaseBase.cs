using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.Searcher
{
    public abstract class SearcherDatabaseBase
    {
        protected const string k_SerializedJsonFile = "/SerializedDatabase.json";
        public string databaseDirectory;

        [SerializeField]
        protected List<SearcherItem> m_ItemList;

        protected SearcherDatabaseBase(string databaseDirectory)
        {
            this.databaseDirectory = databaseDirectory;
        }

        public abstract List<SearcherItem> Search(string query, out float localMaxScore);

        internal void OverwriteId(int newId)
        {
            id = newId;
        }

        internal int id { get; private set; }

        public abstract SearcherItem GetItemFromId(int i);

        protected void LoadFromFile()
        {
            var reader = new System.IO.StreamReader(databaseDirectory + k_SerializedJsonFile);
            string serializedData = reader.ReadToEnd();
            reader.Close();

            EditorJsonUtility.FromJsonOverwrite(serializedData, this);

            foreach (SearcherItem item in m_ItemList)
            {
                item.OverwriteDatabase(this);
                item.ReinitAfterLoadFromFile();
            }
        }

        protected void SerializeToFile()
        {
            if (databaseDirectory == null)
                return;
            string serializedData = EditorJsonUtility.ToJson(this, true);
            var writer = new System.IO.StreamWriter(databaseDirectory + k_SerializedJsonFile, false);
            writer.Write(serializedData);
            writer.Close();
        }

        protected void AddItemToIndex(SearcherItem item, ref int lastId, Action<SearcherItem> action)
        {
            m_ItemList.Insert(lastId, item);

            // We can only set the id here as we only know the final index of the item here.
            item.OverwriteId(lastId);
            item.GeneratePath();

            if (action != null)
                action(item);

            lastId++;

            // This is used for sorting results between databases.
            item.OverwriteDatabase(this);

            if (!item.hasChildren)
                return;

            var childrenIds = new List<int>();
            foreach (SearcherItem child in item.children)
            {
                AddItemToIndex(child, ref lastId, action);
                childrenIds.Add(child.id);
            }

            item.OverwriteChildrenIds(childrenIds);
        }
    }
}
