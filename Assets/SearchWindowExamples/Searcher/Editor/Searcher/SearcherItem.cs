using System;
using System.Collections.Generic;
using Lucene.Net.Documents;
using UnityEngine;

namespace UnityEditor.Searcher
{
    [Serializable]
    public class SearcherItem
    {
        public const string k_IdField = "id";
        public const string k_NameField = "name";
        public const string k_HelpField = "help";
        public const string k_ParentKey = "parents";

        [SerializeField]
        private int m_Id;
        public int id
        {
            get
            {
                return m_Id;
            }
        }

        public string name { get; private set; }
        public string path { get; private set; }
        public string help { get; private set; }

        public int depth
        {
            get
            {
                return parent == null ? 0 : parent.depth + 1;
            }
        }

        public SearcherItem parent { get; private set; }

        public SearcherDatabaseBase database { get; private set; }

        [SerializeField]
        private List<SearcherField> m_Fields;
        public IEnumerable<SearcherField> fields
        {
            get
            {
                return m_Fields;
            }
        }

        [SerializeField]
        private List<int> m_ChildrenIds;
        internal List<int> childrenIds
        {
            get
            {
                return m_ChildrenIds;
            }
        }

        public List<SearcherItem> children { get; private set; }

        public bool hasChildren
        {
            get
            {
                return children.Count > 0;
            }
        }

        public SearcherItem(string name, string help = "", List<SearcherItem> children = null)
        {
            m_Id = -1;
            parent = null;
            database = null;

            m_Fields = new List<SearcherField>();

            this.name = name;
            this.help = help;

            AddField(new SearcherField(k_NameField, name));
            AddField(new SearcherField(k_HelpField, help));

            this.children = new List<SearcherItem>();
            if (children == null)
                return;

            this.children = children;
            foreach (SearcherItem child in children)
                child.OverwriteParent(this);
        }

        public void AddField(SearcherField field)
        {
            if (field == null)
                throw new ArgumentNullException("field is null");

            if (database != null)
                throw new InvalidOperationException(
                    "Cannot add more fields to an item that was already used in a database.");

            if (m_Fields.Exists(f => f.name == field.name))
                throw new InvalidOperationException(
                    "Cannot add a field with the same name as a previously added field.");

            m_Fields.Add(field);
        }

        public void AddChild(SearcherItem child)
        {
            if (child == null)
                throw new ArgumentNullException("child is null");

            if (database != null)
                throw new InvalidOperationException(
                    "Cannot add more children to an item that was already used in a database.");

            if (children == null)
                children = new List<SearcherItem>();

            children.Add(child);
            child.OverwriteParent(this);
        }

        public string GetFieldValue(string fieldName)
        {
            return m_Fields.Find(f => f.name == fieldName).value;
        }

        internal Document GetDocument()
        {
            var doc = new Document();

            var idField = new SearcherField(k_IdField, id.ToString());
            doc.Add(idField.GetField());

            if (path != null)
            {
                var parentsField = new SearcherField(k_ParentKey, path);
                doc.Add(parentsField.GetField());
            }

            foreach (SearcherField field in m_Fields)
            {
                Field luceneField = field.GetField();

                doc.Add(luceneField);
            }

            return doc;
        }

        internal void OverwriteId(int newId)
        {
            m_Id = newId;
        }

        private void OverwriteParent(SearcherItem newParent)
        {
            parent = newParent;
        }

        internal void OverwriteDatabase(SearcherDatabaseBase newDatabase)
        {
            database = newDatabase;
        }

        internal void OverwriteChildrenIds(List<int> childrenIds)
        {
            m_ChildrenIds = childrenIds;
        }

        internal void GeneratePath()
        {
            if (parent != null)
                path = parent.path + " ";
            else
                path = String.Empty;
            path += name;
        }

        internal void ReinitAfterLoadFromFile()
        {
            if (children == null)
                children = new List<SearcherItem>();

            foreach (int id in m_ChildrenIds)
            {
                var child = database.GetItemFromId(id);
                children.Add(child);
                child.OverwriteParent(this);
            }

            name = GetFieldValue(k_NameField);
            help = GetFieldValue(k_HelpField);

            // TODO: shouldn't that be (de)serialized ? add the path as a field ?
            GeneratePath();
        }

        public override string ToString()
        {
            return $"{nameof(id)}: {id}, {nameof(name)}: {name}, {nameof(depth)}: {depth}";
        }
    }
}
