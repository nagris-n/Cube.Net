﻿/* ------------------------------------------------------------------------- */
//
// Copyright (c) 2010 CubeSoft, Inc.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
/* ------------------------------------------------------------------------- */
using System;
using System.Linq;
using System.Threading.Tasks;
using Cube.FileSystem;
using Cube.Net.Rss;

namespace Cube.Net.App.Rss.Reader
{
    /* --------------------------------------------------------------------- */
    ///
    /// RssFacade
    ///
    /// <summary>
    /// RSS フィードに関連する処理の窓口となるクラスです。
    /// </summary>
    /// 
    /* --------------------------------------------------------------------- */
    public sealed class RssFacade : IDisposable
    {
        #region Constructors

        /* ----------------------------------------------------------------- */
        ///
        /// RssFacade
        /// 
        /// <summary>
        /// オブジェクトを初期化します。
        /// </summary>
        /// 
        /// <param name="settings">設定用オブジェクト</param>
        ///
        /* ----------------------------------------------------------------- */
        public RssFacade(SettingsFolder settings)
        {
            Settings = settings;
            if (IO.Exists(Settings.Path)) Settings.Load();
            Settings.AutoSave = true;

            Subscription.IO = Settings.IO;
            Subscription.CacheDirectory = Settings.Cache;
            if (IO.Exists(settings.Feed)) Subscription.Load(settings.Feed);
            Subscription.CollectionChanged += (s, e) => Subscription.Save(Settings.Feed);
            Subscription.SubCollectionChanged += (s, e) => Subscription.Save(Settings.Feed);
            Subscription.Received += WhenReceived;

            Data = new RssBindableData(Subscription, Settings.Value);
        }

        #endregion

        #region Properties

        /* ----------------------------------------------------------------- */
        ///
        /// Data
        /// 
        /// <summary>
        /// バインド可能なデータ一覧を取得します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public RssBindableData Data { get; }

        /* ----------------------------------------------------------------- */
        ///
        /// Settings
        /// 
        /// <summary>
        /// ユーザ設定を取得します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public SettingsFolder Settings { get; }

        /* ----------------------------------------------------------------- */
        ///
        /// Property
        /// 
        /// <summary>
        /// 選択中の Web ページのプロパティを取得または設定します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        private RssEntry Property { get; set; }

        /* ----------------------------------------------------------------- */
        ///
        /// IO
        /// 
        /// <summary>
        /// 入出力用オブジェクトを取得します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        private Operator IO => Settings.IO;

        /* ----------------------------------------------------------------- */
        ///
        /// Subscription
        /// 
        /// <summary>
        /// RSS フィード購読サイトおよびカテゴリ一覧を取得します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        private RssSubscription Subscription { get; } = new RssSubscription();

        #endregion

        #region Methods

        /* ----------------------------------------------------------------- */
        ///
        /// Setup
        ///
        /// <summary>
        /// 初期処理を実行します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        public void Setup()
        {
            var entry = Subscription.GetEntry(Settings.Value.Start);
            if (entry != null)
            {
                Select(entry);
                entry.Parent.Expand();
            }
        }

        /* ----------------------------------------------------------------- */
        ///
        /// NewEntry
        ///
        /// <summary>
        /// 新規 URL を登録します。
        /// </summary>
        /// 
        /// <param name="src">URL</param>
        /// 
        /* ----------------------------------------------------------------- */
        public Task NewEntry(string src) => Subscription.Register(
            src.Contains("://") ?
            new Uri(src) :
            new Uri("http://" + src)
        );

        /* ----------------------------------------------------------------- */
        ///
        /// NewCategory
        ///
        /// <summary>
        /// 新しいカテゴリを生成します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        public void NewCategory() =>
            Select(Subscription.Create(Data.Entry.Value));

        /* ----------------------------------------------------------------- */
        ///
        /// Update
        ///
        /// <summary>
        /// 選択中の RSS フィードの内容を更新します。
        /// </summary>
        /// 
        /// <param name="src">選択中の RSS エントリ</param>
        /// 
        /* ----------------------------------------------------------------- */
        public void Update(RssEntryBase src)
        {
            if (src is RssEntry entry) Subscription.Update(entry.Uri);
            else if (src is RssCategory category)
            {
                Subscription.Update(category.Entries.Select(e => e.Uri));
            }
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Update
        ///
        /// <summary>
        /// 選択中の RSS フィードの内容を更新します。
        /// </summary>
        /// 
        /// <param name="src">選択中の RSS フィード</param>
        /// 
        /* ----------------------------------------------------------------- */
        public void Update(RssFeed src) => Subscription.Update(src?.Uri);

        /* ----------------------------------------------------------------- */
        ///
        /// Move
        /// 
        /// <summary>
        /// 項目を移動します。
        /// </summary>
        /// 
        /// <param name="src">移動元の項目</param>
        /// <param name="dest">移動先のカテゴリ</param>
        /// <param name="index">カテゴリ中の挿入場所</param>
        ///
        /* ----------------------------------------------------------------- */
        public void Move(RssEntryBase src, RssEntryBase dest, int index)
            => Subscription.Move(src, dest, index);

        /* ----------------------------------------------------------------- */
        ///
        /// Remove
        /// 
        /// <summary>
        /// 現在選択中の RSS エントリを削除します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public void Remove()
        {
            if (Data.Entry.Value == null) return;
            Subscription.Remove(Data.Entry.Value);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Rename
        /// 
        /// <summary>
        /// 現在選択中の RSS エントリの名前を変更可能な状態に設定します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public void Rename() => Data.Entry.Value.Editing = true;

        /* ----------------------------------------------------------------- */
        ///
        /// ReadAll
        /// 
        /// <summary>
        /// 現在選択中の RSS エントリ下の全ての記事を既読に設定します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public void ReadAll()
        {
            if (Data.Entry.Value is RssEntry entry) ReadAll(entry);
            else if (Data.Entry.Value is RssCategory category) ReadAll(category);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Reset
        /// 
        /// <summary>
        /// RSS フィードの内容をクリアし、再取得します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        public void Reset()
        {
            if (Data.Entry.Value is RssEntry entry) Subscription.Reset(entry.Uri);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Select
        ///
        /// <summary>
        /// RSS フィードを選択します。
        /// </summary>
        /// 
        /// <param name="src">選択項目</param>
        /// 
        /// <remarks>
        /// 未読アイテムがゼロの場合、選択項目から外れたタイミングで
        /// 全記事を削除しています。これは主にメモリ使用量の抑制を目的と
        /// しています。
        /// </remarks>
        /// 
        /* ----------------------------------------------------------------- */
        public void Select(RssEntryBase src)
        {
            if (src == null) return;
            Data.Entry.Value = src;

            if (src is RssEntry entry && entry != Property)
            {
                var prev = Data.Feed.Value;
                if (prev?.UnreadItems.Count() <= 0) prev.Items.Clear();

                Property = entry;
                Data.Feed.Value = Subscription.GetFeed(entry.Uri);

                var items = Data.Feed.Value?.UnreadItems;
                var first = items?.Count() > 0 ? items.First() : null;
                Select(first);
                Settings.Value.Start = entry.Uri;
            }
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Select
        /// 
        /// <summary>
        /// RSS フィード中の記事を選択します。
        /// </summary>
        /// 
        /// <param name="src">選択項目</param>
        /// 
        /* ----------------------------------------------------------------- */
        public void Select(RssItem src)
        {
            if (src == Data.Article.Value) return;

            var tmp = Data.Article.Value;
            if (Property.SkipContent) Data.Uri.Value = src.Link;
            else Data.Article.Value = src;
            if (tmp != null) tmp.Read = true;
        }

        #region IDisposable

        /* ----------------------------------------------------------------- */
        ///
        /// ~RssFacade
        /// 
        /// <summary>
        /// オブジェクトを破棄します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        ~RssFacade() { Dispose(false); }

        /* ----------------------------------------------------------------- */
        ///
        /// Dispose
        /// 
        /// <summary>
        /// リソースを解放します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Dispose
        /// 
        /// <summary>
        /// リソースを解放します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (Data.Article.Value != null) Data.Article.Value.Read = true;
                Subscription.Dispose();
                Settings.Dispose();
            }
        }

        #endregion

        #endregion

        #region Implementations

        /* ----------------------------------------------------------------- */
        ///
        /// ReadAll
        /// 
        /// <summary>
        /// 指定された RssCategory 下の全ての記事を既読に設定します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        private void ReadAll(RssCategory root)
        {
            foreach (var item in root.Items)
            {
                if (item is RssCategory category) ReadAll(category);
                else if (item is RssEntry entry) ReadAll(entry);
            }
        }

        /* ----------------------------------------------------------------- */
        ///
        /// ReadAll
        /// 
        /// <summary>
        /// 指定された RssEntry 下の全ての記事を既読に設定します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        private void ReadAll(RssEntry entry)
        {
            var feed = Subscription.GetFeed(entry.Uri);
            if (feed == null) return;
            foreach (var article in feed.UnreadItems) article.Read = true;
        }

        /* ----------------------------------------------------------------- */
        ///
        /// WhenReceived
        /// 
        /// <summary>
        /// 新着記事を受信した時に実行されるハンドラです。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        private void WhenReceived(object sender, ValueEventArgs<RssFeed> e) =>
            Data.Message.Value =
                e.Value.Items.Count > 0 ?
                string.Format(Properties.Resources.MessageReceived, e.Value.Items.Count, e.Value.Title ) :
                string.Format(Properties.Resources.MessageNoReceived, e.Value.Title);

        #endregion

        #region Fields
        private bool _disposed = false;
        #endregion
    }
}
