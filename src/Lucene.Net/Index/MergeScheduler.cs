using System;

namespace Lucene.Net.Index
{
    /*
     * Licensed to the Apache Software Foundation (ASF) under one or more
     * contributor license agreements.  See the NOTICE file distributed with
     * this work for additional information regarding copyright ownership.
     * The ASF licenses this file to You under the Apache License, Version 2.0
     * (the "License"); you may not use this file except in compliance with
     * the License.  You may obtain a copy of the License at
     *
     *     http://www.apache.org/licenses/LICENSE-2.0
     *
     * Unless required by applicable law or agreed to in writing, software
     * distributed under the License is distributed on an "AS IS" BASIS,
     * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
     * See the License for the specific language governing permissions and
     * limitations under the License.
     */

    /// <summary>
    /// <p>Expert: <seealso cref="IndexWriter"/> uses an instance
    ///  implementing this interface to execute the merges
    ///  selected by a <seealso cref="MergePolicy"/>.  The default
    ///  MergeScheduler is <seealso cref="ConcurrentMergeScheduler"/>.</p>
    ///  <p>Implementers of sub-classes should make sure that <seealso cref="#clone()"/>
    ///  returns an independent instance able to work with any <seealso cref="IndexWriter"/>
    ///  instance.</p>
    /// @lucene.experimental
    /// </summary>
#if FEATURE_SERIALIZABLE
    [Serializable]
#endif
    public abstract class MergeScheduler : IDisposable, IMergeScheduler
    {
        /// <summary>
        /// Sole constructor. (For invocation by subclass
        ///  constructors, typically implicit.)
        /// </summary>
        protected MergeScheduler()
        {
        }

        /// <summary>
        /// Run the merges provided by <see cref="IndexWriter.NextMerge()"/>. </summary>
        /// <param name="writer"> the <see cref="IndexWriter"/> to obtain the merges from. </param>
        /// <param name="trigger"> the <see cref="MergeTrigger"/> that caused this merge to happen </param>
        /// <param name="newMergesFound"> <c>true</c> iff any new merges were found by the caller; otherwise <c>false</c>
        ///  </param>
        public abstract void Merge(IndexWriter writer, MergeTrigger trigger, bool newMergesFound);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Close this MergeScheduler. </summary>
        protected abstract void Dispose(bool disposing);

        public virtual IMergeScheduler Clone()
        {
            return (MergeScheduler)base.MemberwiseClone();
        }
    }
}