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
using Cube.Net.Rss;
using NUnit.Framework;

namespace Cube.Net.Tests
{
    /* --------------------------------------------------------------------- */
    ///
    /// RssCacheCollectionTest
    ///
    /// <summary>
    /// RssCacheCollection のテスト用クラスです。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestFixture]
    class RssCacheCollectionTest
    {
        /* ----------------------------------------------------------------- */
        ///
        /// Properties_Default
        /// 
        /// <summary>
        /// 各種プロパティの初期値を確認します。
        /// </summary>
        /// 
        /* ----------------------------------------------------------------- */
        [Test]
        public void Properties_Default()
        {
            var collection = new RssCacheCollection();

            Assert.That(collection.Count,        Is.EqualTo(0));
            Assert.That(collection.IsReadOnly,   Is.False);
            Assert.That(collection.Keys.Count,   Is.EqualTo(0));
            Assert.That(collection.Values.Count, Is.EqualTo(0));
        }
    }
}
