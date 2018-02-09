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
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cube.FileSystem;
using Cube.Net.Rss;
using Cube.Tasks;
using Cube.Xui;

namespace Cube.Net.App.Rss.Reader
{
    /* --------------------------------------------------------------------- */
    ///
    /// RssSubscriber
    ///
    /// <summary>
    /// 購読フィード一覧を管理するクラスです。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public sealed class RssSubscriber :
        IEnumerable<IRssEntry>, INotifyCollectionChanged, IDisposable
    {
        #region Constructors

        /* ----------------------------------------------------------------- */
        ///
        /// RssSubscription
        ///
        /// <summary>
        /// オブジェクトを初期化します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public RssSubscriber()
        {
            _dispose = new OnceAction<bool>(Dispose);

            _tree.IsSynchronous = true;
            _tree.CollectionChanged += (s, e) =>
            {
                AutoSaveCore();
                CollectionChanged?.Invoke(this, e);
            };

            _monitors[0] = new RssMonitor() { Interval = TimeSpan.FromHours(1) };
            _monitors[0].Subscribe(e => Received?.Invoke(this, ValueEventArgs.Create(e)));

            _monitors[1] = new RssMonitor() { Interval = TimeSpan.FromHours(24) };
            _monitors[1].Subscribe(e => Received?.Invoke(this, ValueEventArgs.Create(e)));

            _autosaver.AutoReset = false;
            _autosaver.Interval = 1000.0;
            _autosaver.Elapsed += WhenAutoSaved;
        }

        #endregion

        #region Properties

        /* ----------------------------------------------------------------- */
        ///
        /// FileName
        ///
        /// <summary>
        /// RSS エントリ一覧が保存されている JSON ファイルのパスを取得
        /// または設定します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public string FileName { get; set; }

        /* ----------------------------------------------------------------- */
        ///
        /// IO
        ///
        /// <summary>
        /// 入出力用のオブジェクトを取得または設定します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public Operator IO
        {
            get => _feeds.IO;
            set => _feeds.IO = value;
        }

        /* ----------------------------------------------------------------- */
        ///
        /// CacheDirectory
        ///
        /// <summary>
        /// キャッシュ用ディレクトリのパスを取得または設定します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public string CacheDirectory
        {
            get => _feeds.Directory;
            set => _feeds.Directory = value;
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Categories
        ///
        /// <summary>
        /// カテゴリ一覧を取得します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public IEnumerable<RssCategory> Categories => this.OfType<RssCategory>();

        /* ----------------------------------------------------------------- */
        ///
        /// Entries
        ///
        /// <summary>
        /// どのカテゴリにも属さない RSS エントリ一覧を取得します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public IEnumerable<RssEntry> Entries => this.OfType<RssEntry>();

        #endregion

        #region Events

        /* ----------------------------------------------------------------- */
        ///
        /// CollectionChanged
        ///
        /// <summary>
        /// コレクション変更時に発生するイベントです。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        /* ----------------------------------------------------------------- */
        ///
        /// Received
        ///
        /// <summary>
        /// 新着記事を受信した時に発生するイベントです。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public event ValueEventHandler<RssFeed> Received;

        #endregion

        #region Methods

        #region Entry

        /* ----------------------------------------------------------------- */
        ///
        /// Find
        ///
        /// <summary>
        /// URL に対応するオブジェクトを取得します。
        /// </summary>
        ///
        /// <param name="uri">URL</param>
        ///
        /// <returns>対応するオブジェクト</returns>
        ///
        /* ----------------------------------------------------------------- */
        public RssEntry Find(Uri uri) =>
            uri != null && _feeds.ContainsKey(uri) ? _feeds[uri] as RssEntry : null;

        /* ----------------------------------------------------------------- */
        ///
        /// Create
        ///
        /// <summary>
        /// 新しいカテゴリを生成して挿入します。
        /// </summary>
        ///
        /// <param name="src">挿入位置</param>
        ///
        /// <returns>カテゴリ</returns>
        ///
        /* ----------------------------------------------------------------- */
        public RssCategory Create(IRssEntry src)
        {
            var parent = src is RssCategory rc ? rc : src?.Parent as RssCategory;
            var dest   = new RssCategory
            {
                Title   = Properties.Resources.MessageNewCategory,
                Parent  = parent,
                Editing = true,
            };

            var items = parent != null ? parent.Children : _tree;
            var count = parent != null ? parent.Entries.Count() : Entries.Count();
            items.Insert(items.Count - count, dest);
            parent.Expand();

            return dest;
        }

        /* ----------------------------------------------------------------- */
        ///
        /// RegisterAsync
        ///
        /// <summary>
        /// 新しい RSS フィード URL を非同期で登録します。
        /// </summary>
        ///
        /// <param name="uri">URL オブジェクト</param>
        ///
        /* ----------------------------------------------------------------- */
        public async Task RegisterAsync(Uri uri)
        {
            var rss = await _client.GetAsync(uri).ConfigureAwait(false);
            if (rss == null) throw Properties.Resources.ErrorFeedNotFound.ToException();
            if (_feeds.ContainsKey(rss.Uri)) throw Properties.Resources.ErrorFeedAlreadyExists.ToException();

            AddCore(new RssEntry(rss));
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Add
        ///
        /// <summary>
        /// 新しい RSS エントリ一覧を追加します。
        /// </summary>
        ///
        /// <param name="src">RSS エントリ一覧</param>
        ///
        /* ----------------------------------------------------------------- */
        public void Add(IEnumerable<IRssEntry> src)
        {
            System.Diagnostics.Debug.Assert(src != null);

            foreach (var item in src)
            {
                if (item is RssCategory rc) AddCore(rc);
                else if (item is RssEntry re) AddCore(re);
            }
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Remove
        ///
        /// <summary>
        /// RSS エントリを削除します。
        /// </summary>
        ///
        /// <param name="src">削除する RSS フィード</param>
        ///
        /* ----------------------------------------------------------------- */
        public void Remove(IRssEntry src)
        {
            if (src is RssCategory rc) RemoveCore(rc);
            else if (src is RssEntry re) RemoveCore(re);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Clear
        ///
        /// <summary>
        /// 全ての項目を削除します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public void Clear()
        {
            foreach (var e in _tree.ToArray()) Remove(e);
        }

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
        public void Move(IRssEntry src, IRssEntry dest, int index)
        {
            if (src.Parent is RssCategory rc) rc.Children.Remove(src);
            else _tree.Remove(src);

            var parent = src is RssEntry && dest is RssCategory ?
                         dest as RssCategory :
                         dest?.Parent as RssCategory;
            var items  = parent?.Children ?? _tree;
            src.Parent = parent;
            if (index < 0 || index >= items.Count) items.Add(src);
            else items.Insert(index, src);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Load
        ///
        /// <summary>
        /// 設定ファイルを読み込みます。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public void Load() => Add(RssOperator.Load(FileName, IO).SelectMany(e =>
            !string.IsNullOrEmpty(e.Title) ?
            new[] { e as IRssEntry } :
            e.Entries.Select(re =>
            {
                re.Parent = null;
                return re as IRssEntry;
            })
        ));

        /* ----------------------------------------------------------------- */
        ///
        /// Save
        ///
        /// <summary>
        /// 設定ファイルに保存します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public void Save() => Categories.Concat(new[] { new RssCategory
        {
            Title    = string.Empty,
            Children = Entries.Cast<IRssEntry>().ToBindable(),
        }}).Save(FileName, IO);

        #endregion

        #region Monitor

        /* ----------------------------------------------------------------- */
        ///
        /// Start
        ///
        /// <summary>
        /// 監視を開始します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public void Start()
        {
            _monitors[0].Start(TimeSpan.FromSeconds(3));
            _monitors[1].Start(TimeSpan.FromMinutes(1));
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Stop
        ///
        /// <summary>
        /// 監視を停止します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public void Stop()
        {
            foreach (var mon in _monitors) mon.Stop();
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Suspend
        ///
        /// <summary>
        /// 監視を一時停止します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        public void Suspend()
        {
            foreach (var mon in _monitors) mon.Suspend();
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Update
        ///
        /// <summary>
        /// RSS フィードの内容を更新します。
        /// </summary>
        ///
        /// <param name="uris">対象とするフィード URL 一覧</param>
        ///
        /* ----------------------------------------------------------------- */
        public void Update(params Uri[] uris)
        {
            var m0 = uris.Where(e => _monitors[0].Contains(e));
            if (m0.Count() > 0) _monitors[0].Update(m0);

            var m1 = uris.Where(e => _monitors[1].Contains(e));
            if (m1.Count() > 0) _monitors[1].Update(m1);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Reset
        ///
        /// <summary>
        /// RSS フィードの内容をクリアし、再取得します。
        /// </summary>
        ///
        /// <param name="uri">対象とするフィード URL</param>
        ///
        /* ----------------------------------------------------------------- */
        public void Reset(Uri uri)
        {
            var dest = Find(uri);
            if (dest == null) return;

            _feeds.DeleteCache(uri);
            dest.Items.Clear();
            dest.Count = 0;
            dest.LastChecked = null;
            Update(uri);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Reschedule
        ///
        /// <summary>
        /// RSS フィードのチェック方法を再設定します。
        /// </summary>
        ///
        /// <param name="src">対象とする RSS フィード</param>
        ///
        /* ----------------------------------------------------------------- */
        public void Reschedule(RssEntry src)
        {
            var now  = DateTime.Now;
            var dest = src.IsHighFrequency(now) ? _monitors[0] :
                       src.IsLowFrequency(now)  ? _monitors[1] : null;

            foreach (var mon in _monitors)
            {
                if (!mon.Contains(src.Uri)) continue;
                if (mon == dest) return;
                mon.Remove(src.Uri);
                break;
            }

            dest?.Register(src.Uri, src.LastChecked);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Set
        ///
        /// <summary>
        /// RSS フィードのチェック間隔を設定します。
        /// </summary>
        ///
        /// <param name="kind">種類</param>
        /// <param name="time">チェック間隔</param>
        ///
        /* ----------------------------------------------------------------- */
        public void Set(RssCheckFrequency kind, TimeSpan? time)
        {
            var mon = kind == RssCheckFrequency.High ? _monitors[0] :
                      kind == RssCheckFrequency.Low  ? _monitors[1] : null;

            if (mon != null && time.HasValue && !mon.Interval.Equals(time))
            {
                mon.Stop();
                mon.Interval = time.Value;
                mon.Start(time.Value);
            }
        }

        #endregion

        #region IEnumarable<IRssEntry>

        /* ----------------------------------------------------------------- */
        ///
        /// GetEnumerator
        ///
        /// <summary>
        /// 反復用オブジェクトを取得します。
        /// </summary>
        ///
        /// <returns>反復用オブジェクト</returns>
        ///
        /* ----------------------------------------------------------------- */
        public IEnumerator<IRssEntry> GetEnumerator() => _tree.GetEnumerator();

        /* ----------------------------------------------------------------- */
        ///
        /// GetEnumerator
        ///
        /// <summary>
        /// 反復用オブジェクトを取得します。
        /// </summary>
        ///
        /// <returns>反復用オブジェクト</returns>
        ///
        /* ----------------------------------------------------------------- */
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion

        #region IDisposable

        /* ----------------------------------------------------------------- */
        ///
        /// ~RssSubscribeCollection
        ///
        /// <summary>
        /// オブジェクトを破棄します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        ~RssSubscriber() { _dispose.Invoke(false); }

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
            _dispose.Invoke(true);
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
            if (disposing)
            {
                foreach (var mon in _monitors) mon.Dispose();
                _feeds.Dispose();
            }
        }

        #endregion

        #endregion

        #region Implementations

        /* ----------------------------------------------------------------- */
        ///
        /// AddCore
        ///
        /// <summary>
        /// カテゴリおよびカテゴリ中の RSS エントリを全て追加します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        private void AddCore(RssCategory src)
        {
            foreach (var re in src.Entries) AddCore(re);
            foreach (var rc in src.Categories) AddCore(rc);

            src.Children.CollectionChanged -= WhenChildrenChanged;
            src.Children.CollectionChanged += WhenChildrenChanged;

            if (src.Parent == null) _tree.Add(src);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// AddCore
        ///
        /// <summary>
        /// RSS エントリを追加します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        private void AddCore(RssEntry src)
        {
            if (_feeds.ContainsKey(src.Uri)) return;
            src.Items = src.Items.ToBindable(_context);

            _feeds.Add(src);
            if (src.Parent == null) _tree.Add(src);
            Reschedule(src);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// RemoveCore
        ///
        /// <summary>
        /// カテゴリおよびカテゴリ中の RSS エントリを全て削除します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        private void RemoveCore(RssCategory src)
        {
            src.Children.CollectionChanged -= WhenChildrenChanged;

            foreach (var item in src.Children.ToList())
            {
                if (item is RssCategory c) RemoveCore(c);
                else if (item is RssEntry e) RemoveCore(e);
            }

            if (src.Parent is RssCategory rc) rc.Children.Remove(src);
            else _tree.Remove(src);
            src.Dispose();
        }

        /* ----------------------------------------------------------------- */
        ///
        /// RemoveCore
        ///
        /// <summary>
        /// RSS エントリを削除します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        private void RemoveCore(RssEntry src)
        {
            foreach (var mon in _monitors) mon.Remove(src.Uri);

            _feeds.Remove(src.Uri);

            if (src.Parent is RssCategory rc) rc.Children.Remove(src);
            else _tree.Remove(src);
            src.Dispose();
        }

        /* ----------------------------------------------------------------- */
        ///
        /// AutoSaveCore
        ///
        /// <summary>
        /// 自動保存を実行します。
        /// </summary>
        ///
        /* ----------------------------------------------------------------- */
        private void AutoSaveCore()
        {
            _autosaver.Stop();
            _autosaver.Interval = 1000.0;
            _autosaver.Start();
        }

        #endregion

        #region Handlers
        private void WhenChildrenChanged(object s, EventArgs e) => AutoSaveCore();
        private void WhenAutoSaved(object s, EventArgs e) => Task.Run(() => Save()).Forget();
        #endregion

        #region Fields
        private OnceAction<bool> _dispose;
        private BindableCollection<IRssEntry> _tree = new BindableCollection<IRssEntry>();
        private RssCacheDictionary _feeds = new RssCacheDictionary();
        private RssMonitor[] _monitors = new RssMonitor[2];
        private RssClient _client = new RssClient();
        private SynchronizationContext _context = SynchronizationContext.Current;
        private System.Timers.Timer _autosaver = new System.Timers.Timer();
        #endregion
    }
}
