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
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Cube.Log;

namespace Cube.Net.Http
{
    /* --------------------------------------------------------------------- */
    ///
    /// HttpMonitorBase
    ///
    /// <summary>
    /// 定期的に HTTP 通信を実行するための基底クラスです。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public abstract class HttpMonitorBase<TValue> : NetworkMonitorBase
    {
        #region Constructors

        /* ----------------------------------------------------------------- */
        ///
        /// HttpMonitorBase
        ///
        /// <summary>
        /// オブジェクトを初期化します。
        /// </summary>
        /// 
        /// <param name="handler">HTTP 通信用ハンドラ</param>
        ///
        /* ----------------------------------------------------------------- */
        protected HttpMonitorBase(ContentHandler<TValue> handler) : base()
        {
            Handler = handler;
            Timeout = TimeSpan.FromSeconds(2);
            _http   = HttpClientFactory.Create(handler);
        }

        #endregion

        #region Properties

        /* --------------------------------------------------------------------- */
        ///
        /// UserAgent
        /// 
        /// <summary>
        /// ユーザエージェントを取得または設定します。
        /// </summary>
        ///
        /* --------------------------------------------------------------------- */
        public string UserAgent
        {
            get => Handler.UserAgent;
            set => Handler.UserAgent = value;
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Handler
        ///
        /// <summary>
        /// HTTP ハンドラを取得します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        protected ContentHandler<TValue> Handler { get; }

        /* ----------------------------------------------------------------- */
        ///
        /// Subscriptions
        ///
        /// <summary>
        /// 購読者一覧を取得します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        protected IList<Func<Uri, TValue, Task>> Subscriptions { get; }
            = new List<Func<Uri, TValue, Task>>();

        #endregion

        #region Methods

        /* ----------------------------------------------------------------- */
        ///
        /// Subscribe
        ///
        /// <summary>
        /// データ受信時に非同期実行する処理を登録します。
        /// </summary>
        /// 
        /// <param name="action">非同期実行する処理</param>
        /// 
        /// <returns>登録解除用オブジェクト</returns>
        /// 
        /* ----------------------------------------------------------------- */
        public IDisposable Subscribe(Func<Uri, TValue, Task> action)
        {
            Subscriptions.Add(action);
            return Disposable.Create(() => Subscriptions.Remove(action));
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Subscribe
        ///
        /// <summary>
        /// データ受信時に実行する処理を登録します。
        /// </summary>
        /// 
        /// <param name="action">実行する処理</param>
        /// 
        /// <returns>登録解除用オブジェクト</returns>
        /// 
        /* ----------------------------------------------------------------- */
        public IDisposable Subscribe(Action<Uri, TValue> action)
            => Subscribe(async (u, v) =>
        {
            action(u, v);
            await Task.FromResult(0);
        });

        /* ----------------------------------------------------------------- */
        ///
        /// Publish
        ///
        /// <summary>
        /// 新しい結果を発行します。
        /// </summary>
        /// 
        /// <param name="uri">URL</param>
        /// <param name="value">通信結果</param>
        ///
        /* ----------------------------------------------------------------- */
        protected virtual async Task Publish(Uri uri, TValue value)
        {
            foreach (var action in Subscriptions)
            {
                try { await action(uri, value); }
                catch (Exception err) { this.LogWarn(err.ToString(), err); }
            }
        }

        /* ----------------------------------------------------------------- */
        ///
        /// PublishAsync
        ///
        /// <summary>
        /// HTTP 通信を実行し、変換結果を引数にして Publish メソッドを
        /// 実行します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        protected async Task PublishAsync(Uri uri)
        {
            if (State != TimerState.Run) return;

            using (var response = await GetAsync(uri))
            {
                var status = response.StatusCode;
                var code = (int)status;
                var digit = code / 100;

                if (response.Content is HttpValueContent<TValue> c) await Publish(uri, c.Value); // OK
                else if (digit == 3) this.LogDebug($"HTTP:{code} {status}");
                else if (digit == 4) LogWarn(uri, $"HTTP:{code} {status}");
                else if (digit == 5) throw new HttpRequestException($"HTTP:{code} {status}");
                else LogWarn(uri, $"Content is not {nameof(TValue)} ({code})");
            }
        }

        /* ----------------------------------------------------------------- */
        ///
        /// GetAsync
        ///
        /// <summary>
        /// HTTP 通信を実行します。
        /// </summary>
        /// 
        /// <param name="uri">URL</param>
        /// 
        /// <returns>通信結果</returns>
        /// 
        /* ----------------------------------------------------------------- */
        protected async Task<HttpResponseMessage> GetAsync(Uri uri)
        {
            SetTimeout();
            return await _http.GetAsync(uri, HttpCompletionOption.ResponseContentRead);
        }

        /* ----------------------------------------------------------------- */
        ///
        /// Dispose
        ///
        /// <summary>
        /// リソースを解放します。
        /// </summary>
        /// 
        /// <param name="disposing">
        /// マネージリソースを解放するかどうかを示す値
        /// </param>
        /// 
        /* ----------------------------------------------------------------- */
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _http.Dispose();
                Handler.Dispose();
            }
        }

        #endregion

        #region Implementations

        /* ----------------------------------------------------------------- */
        ///
        /// SetTimeout
        ///
        /// <summary>
        /// タイムアウト時間を設定します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        private void SetTimeout()
        {
            try { if (_http.Timeout != Timeout) _http.Timeout = Timeout; }
            catch (Exception /* err */) { this.LogWarn("Timeout cannot be applied"); }
        }

        /* ----------------------------------------------------------------- */
        ///
        /// LogWarn
        ///
        /// <summary>
        /// エラー内容をログに出力します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        private void LogWarn(Uri uri, string message)
        {
            this.LogWarn(uri.ToString());
            this.LogWarn(message);
        }

        #region Fields
        private HttpClient _http;
        #endregion

        #endregion
    }
}