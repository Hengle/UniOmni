using System;
using UnityEngine;
using Lucene.Net.Documents;

namespace UnityEditor.Searcher
{
    [Serializable]
    public class SearcherField
    {
        // TODO: terrible naming, as it actually changes if the field is analyzed/normalized
        public enum Indexed
        {
            INDEXED,
            NOT_INDEXED
        }

        [SerializeField]
        private string m_Name;
        public string name
        {
            get
            {
                return m_Name;
            }
        }

        [SerializeField]
        private string m_Value;
        public string value
        {
            get
            {
                return m_Value;
            }
            internal set
            {
                m_Value = value;
            }
        }

        [SerializeField]
        private Indexed m_Indexed;
        public Indexed indexed
        {
            get
            {
                return m_Indexed;
            }
        }

        public SearcherField(string name, string value, Indexed indexed = Indexed.INDEXED)
        {
            m_Name = name;
            m_Value = value;
            m_Indexed = indexed;
        }

        internal Field GetField()
        {
            return new Field(
                m_Name, m_Value,
                Field.Store.YES,
                m_Indexed == Indexed.INDEXED
                    ? Field.Index.ANALYZED
                    : Field.Index.NOT_ANALYZED );
        }
    }
}