using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Application = UnityEngine.Application;

namespace UnityEditor.Searcher
{
    public class SearcherWindow : EditorWindow
    {
        private const string k_DatabaseDirectory = "/../Library/Searcher";

        private static readonly Vector2 k_MinSize = new Vector2(300, 150);

        private static Vector2 s_DefaultSize = new Vector2(500, 300);
        private static IEnumerable<SearcherItem> s_Items;
        private static Searcher s_Searcher;
        private static Func<SearcherItem, bool> s_ItemSelectedDelegate;

        private Action<SearcherControl.AnalyticsEvent> m_AnalyticsDataDelegate;

        private SearcherControl m_SearcherControl;

        private Vector2 m_OriginalMousePos;
        private Rect m_OriginalWindowPos;
        private Rect m_NewWindowPos;
        private bool m_IsMouseDownOnResizer;
        private bool m_IsMouseDownOnTitle;
        private Focusable m_focusedBefore;

        public static void Show(
            EditorWindow host,
            IList<SearcherItem> items,
            string title,
            Func<SearcherItem, bool> itemSelectedDelegate,
            Vector2 displayPosition)
        {
            Show(host, items, title, Application.dataPath + k_DatabaseDirectory, itemSelectedDelegate, displayPosition);
        }

        public static void Show(
            EditorWindow host,
            IList<SearcherItem> items,
            ISearcherAdapter adapter,
            Func<SearcherItem, bool> itemSelectedDelegate,
            Vector2 displayPosition,
            Action<SearcherControl.AnalyticsEvent> analyticsDataDelegate)
        {
            Show(host, items, adapter, Application.dataPath + k_DatabaseDirectory, itemSelectedDelegate,
                displayPosition, analyticsDataDelegate);
        }

        public static void Show(
            EditorWindow host,
            IList<SearcherItem> items,
            string title,
            string directoryPath,
            Func<SearcherItem, bool> itemSelectedDelegate,
            Vector2 displayPosition)
        {
            s_Items = items;
            var databaseDir = directoryPath;
            var database = LuceneDatabase.Create(s_Items.ToList(), databaseDir);
            s_Searcher = new Searcher(database, title);

            Show(host, s_Searcher, itemSelectedDelegate, displayPosition, null);
        }

        public static void Show(
            EditorWindow host,
            IEnumerable<SearcherItem> items,
            ISearcherAdapter adapter,
            string directoryPath,
            Func<SearcherItem, bool> itemSelectedDelegate,
            Vector2 displayPosition,
            Action<SearcherControl.AnalyticsEvent> analyticsDataDelegate)
        {
            s_Items = items;
            var databaseDir = directoryPath;
            var database = SearcherDatabase.Create(s_Items.ToList(), databaseDir);
            s_Searcher = new Searcher(database, adapter);

            Show(host, s_Searcher, itemSelectedDelegate, displayPosition, analyticsDataDelegate);
        }

        public static void Show(
            EditorWindow host,
            Searcher searcher,
            Func<SearcherItem, bool> itemSelectedDelegate,
            Vector2 displayPosition,
            Action<SearcherControl.AnalyticsEvent> analyticsDataDelegate)
        {
            s_Searcher = searcher;
            s_ItemSelectedDelegate = itemSelectedDelegate;

            var window = CreateInstance<SearcherWindow>();
            window.m_AnalyticsDataDelegate = analyticsDataDelegate;
            var position = GetPosition(host, displayPosition);
            window.position = new Rect(position + host.position.position, s_DefaultSize);
            window.ShowPopup();
            window.Focus();
        }

        private static Vector2 GetPosition(EditorWindow host, Vector2 displayPosition)
        {
            var x = displayPosition.x;
            var y = displayPosition.y;

            // Searcher overlaps with the right boundary.
            if (x + s_DefaultSize.x >= host.position.size.x)
                x -= s_DefaultSize.x;

            // The displayPosition should be in window world space but the
            // EditorWindow.position is actually the rootVisualElement
            // rectangle, not including the tabs area. So we need to do a
            // small correction here.
            y -= host.rootVisualElement.resolvedStyle.top;

            // Searcher overlaps with the bottom boundary.
            if (y + s_DefaultSize.y >= host.position.size.y)
                y -= s_DefaultSize.y;

            return new Vector2(x, y);
        }

        private void OnEnable()
        {
            m_SearcherControl = new SearcherControl();
            m_SearcherControl.Setup(s_Searcher, SelectionCallback, OnAnalyticsDataCallback);

            m_SearcherControl.m_TitleLabel.RegisterCallback<MouseDownEvent>(OnTitleMouseDown);
            m_SearcherControl.m_TitleLabel.RegisterCallback<MouseUpEvent>(OnTitleMouseUp);

            m_SearcherControl.m_Resizer.RegisterCallback<MouseDownEvent>(OnResizerMouseDown);
            m_SearcherControl.m_Resizer.RegisterCallback<MouseUpEvent>(OnResizerMouseUp);

            var root = this.rootVisualElement;
            root.style.flexGrow = 1;
            root.Add(m_SearcherControl);
        }

        private void OnDisable()
        {
            m_SearcherControl.m_TitleLabel.UnregisterCallback<MouseDownEvent>(OnTitleMouseDown);
            m_SearcherControl.m_TitleLabel.UnregisterCallback<MouseUpEvent>(OnTitleMouseUp);

            m_SearcherControl.m_Resizer.UnregisterCallback<MouseDownEvent>(OnResizerMouseDown);
            m_SearcherControl.m_Resizer.UnregisterCallback<MouseUpEvent>(OnResizerMouseUp);
        }

        private void OnTitleMouseDown(MouseDownEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse)
                return;

            m_IsMouseDownOnTitle = true;

            m_NewWindowPos = position;
            m_OriginalWindowPos = position;
            m_OriginalMousePos = evt.mousePosition;

            m_focusedBefore = rootVisualElement.panel.focusController.focusedElement;

            m_SearcherControl.m_TitleLabel.RegisterCallback<MouseMoveEvent>(OnTitleMouseMove);
            m_SearcherControl.m_TitleLabel.RegisterCallback<KeyDownEvent>(OnSearcherKeyDown);
            m_SearcherControl.m_TitleLabel.CaptureMouse();
        }

        private void OnTitleMouseUp(MouseUpEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse)
                return;

            if (!m_SearcherControl.m_TitleLabel.HasMouseCapture())
                return;

            FinishMove();
        }

        private void FinishMove()
        {
            m_SearcherControl.m_TitleLabel.UnregisterCallback<MouseMoveEvent>(OnTitleMouseMove);
            m_SearcherControl.m_TitleLabel.UnregisterCallback<KeyDownEvent>(OnSearcherKeyDown);
            m_SearcherControl.m_TitleLabel.ReleaseMouse();
            m_focusedBefore?.Focus();
            m_IsMouseDownOnTitle = false;
        }

        private void OnTitleMouseMove(MouseMoveEvent evt)
        {
            var delta = evt.mousePosition - m_OriginalMousePos;
            m_NewWindowPos = new Rect(position.position + delta, position.size);
            Repaint();
        }

        private void OnResizerMouseDown(MouseDownEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse)
                return;

            m_IsMouseDownOnResizer = true;

            m_NewWindowPos = position;
            m_OriginalWindowPos = position;
            m_OriginalMousePos = evt.mousePosition;

            m_focusedBefore = rootVisualElement.panel.focusController.focusedElement;

            m_SearcherControl.m_Resizer.RegisterCallback<MouseMoveEvent>(OnResizerMouseMove);
            m_SearcherControl.m_Resizer.RegisterCallback<KeyDownEvent>(OnSearcherKeyDown);
            m_SearcherControl.m_Resizer.CaptureMouse();
        }

        private void OnResizerMouseUp(MouseUpEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse)
                return;

            if (!m_SearcherControl.m_Resizer.HasMouseCapture())
                return;

            FinishResize();
        }

        void FinishResize()
        {
            m_SearcherControl.m_Resizer.UnregisterCallback<MouseMoveEvent>(OnResizerMouseMove);
            m_SearcherControl.m_Resizer.UnregisterCallback<KeyDownEvent>(OnSearcherKeyDown);
            m_SearcherControl.m_Resizer.ReleaseMouse();
            m_focusedBefore?.Focus();
            m_IsMouseDownOnResizer = false;
        }

        private void OnResizerMouseMove(MouseMoveEvent evt)
        {
            var delta = evt.mousePosition - m_OriginalMousePos;
            s_DefaultSize = m_OriginalWindowPos.size + delta;

            if (s_DefaultSize.x < k_MinSize.x)
                s_DefaultSize.x = k_MinSize.x;

            if (s_DefaultSize.y < k_MinSize.y)
                s_DefaultSize.y = k_MinSize.y;

            m_NewWindowPos = new Rect(position.position, s_DefaultSize);
            Repaint();
        }

        void OnSearcherKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                if (m_IsMouseDownOnTitle)
                {
                    FinishMove();
                    position = m_OriginalWindowPos;
                }
                else if (m_IsMouseDownOnResizer)
                {
                    FinishResize();
                    position = m_OriginalWindowPos;
                }
            }
        }

        private void OnGUI()
        {
            if ((m_IsMouseDownOnTitle || m_IsMouseDownOnResizer) && Event.current.type == EventType.Layout)
                position = m_NewWindowPos;
        }

        private void SelectionCallback(SearcherItem item)
        {
            if (s_ItemSelectedDelegate == null || s_ItemSelectedDelegate(item))
                Close();
        }

        private void OnAnalyticsDataCallback(SearcherControl.AnalyticsEvent item)
        {
            if (m_AnalyticsDataDelegate != null)
                m_AnalyticsDataDelegate(item);
        }

        private void OnLostFocus()
        {
            if (m_IsMouseDownOnTitle)
            {
                FinishMove();
            }
            else if (m_IsMouseDownOnResizer)
            {
                FinishResize();
            }

            // TODO: HACK - ListView's scroll view steals focus using the scheduler.
            EditorApplication.update += HackDueToCloseOnLostFocusCrashing;
        }

        // See: https://fogbugz.unity3d.com/f/cases/1004504/
        private void HackDueToCloseOnLostFocusCrashing()
        {
            // Notify user that the searcher action was cancelled.
            if (s_ItemSelectedDelegate != null)
                s_ItemSelectedDelegate(null);

            Close();
            EditorApplication.update -= HackDueToCloseOnLostFocusCrashing;
        }
    }
}