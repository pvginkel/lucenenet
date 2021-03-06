using Lucene.Net.Util;
using System;

namespace Lucene.Net.Search
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

    using AtomicReaderContext = Lucene.Net.Index.AtomicReaderContext;
    using Entry = Lucene.Net.Search.FieldValueHitQueue.Entry;

    /// <summary>
    /// A <seealso cref="ICollector"/> that sorts by <seealso cref="SortField"/> using
    /// <seealso cref="FieldComparer"/>s.
    /// <p/>
    /// See the <seealso cref="#create(Lucene.Net.Search.Sort, int, boolean, boolean, boolean, boolean)"/> method
    /// for instantiating a TopFieldCollector.
    ///
    /// @lucene.experimental
    /// </summary>
#if FEATURE_SERIALIZABLE
    [Serializable]
#endif
    public abstract class TopFieldCollector : TopDocsCollector<Entry>
    {
        // TODO: one optimization we could do is to pre-fill
        // the queue with sentinel value that guaranteed to
        // always compare lower than a real hit; this would
        // save having to check queueFull on each insert

        /*
         * Implements a TopFieldCollector over one SortField criteria, without
         * tracking document scores and maxScore.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private class OneComparerNonScoringCollector : TopFieldCollector
        {
            internal FieldComparer comparer;
            internal readonly int reverseMul;
            internal readonly FieldValueHitQueue<Entry> queue;

            public OneComparerNonScoringCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
                this.queue = queue;
                comparer = queue.Comparers[0];
                reverseMul = queue.ReverseMul[0];
            }

            internal void UpdateBottom(int doc)
            {
                // bottom.score is already set to Float.NaN in add().
                bottom.Doc = docBase + doc;
                bottom = m_pq.UpdateTop();
            }

            public override void Collect(int doc)
            {
                ++m_totalHits;
                if (queueFull)
                {
                    if ((reverseMul * comparer.CompareBottom(doc)) <= 0)
                    {
                        // since docs are visited in doc Id order, if compare is 0, it means
                        // this document is larger than anything else in the queue, and
                        // therefore not competitive.
                        return;
                    }

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    comparer.Copy(bottom.Slot, doc);
                    UpdateBottom(doc);
                    comparer.SetBottom(bottom.Slot);
                }
                else
                {
                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    comparer.Copy(slot, doc);
                    Add(slot, doc, float.NaN);
                    if (queueFull)
                    {
                        comparer.SetBottom(bottom.Slot);
                    }
                }
            }

            public override void SetNextReader(AtomicReaderContext context)
            {
                this.docBase = context.DocBase;
                queue.SetComparer(0, comparer.SetNextReader(context));
                comparer = queue.FirstComparer;
            }
            
            public override void SetScorer(Scorer scorer)
            {
                comparer.SetScorer(scorer);
            }
        }

        /*
         * Implements a TopFieldCollector over one SortField criteria, without
         * tracking document scores and maxScore, and assumes out of orderness in doc
         * Ids collection.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private class OutOfOrderOneComparerNonScoringCollector : OneComparerNonScoringCollector
        {
            public OutOfOrderOneComparerNonScoringCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
            }

            public override void Collect(int doc)
            {
                ++m_totalHits;
                if (queueFull)
                {
                    // Fastmatch: return if this hit is not competitive
                    int cmp = reverseMul * comparer.CompareBottom(doc);
                    if (cmp < 0 || (cmp == 0 && doc + docBase > bottom.Doc))
                    {
                        return;
                    }

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    comparer.Copy(bottom.Slot, doc);
                    UpdateBottom(doc);
                    comparer.SetBottom(bottom.Slot);
                }
                else
                {
                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    comparer.Copy(slot, doc);
                    Add(slot, doc, float.NaN);
                    if (queueFull)
                    {
                        comparer.SetBottom(bottom.Slot);
                    }
                }
            }

            public override bool AcceptsDocsOutOfOrder
            {
                get { return true; }
            }
        }

        /*
         * Implements a TopFieldCollector over one SortField criteria, while tracking
         * document scores but no maxScore.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private class OneComparerScoringNoMaxScoreCollector : OneComparerNonScoringCollector
        {
            internal Scorer scorer;

            public OneComparerScoringNoMaxScoreCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
            }

            internal void UpdateBottom(int doc, float score)
            {
                bottom.Doc = docBase + doc;
                bottom.Score = score;
                bottom = m_pq.UpdateTop();
            }

            public override void Collect(int doc)
            {
                ++m_totalHits;
                if (queueFull)
                {
                    if ((reverseMul * comparer.CompareBottom(doc)) <= 0)
                    {
                        // since docs are visited in doc Id order, if compare is 0, it means
                        // this document is largest than anything else in the queue, and
                        // therefore not competitive.
                        return;
                    }

                    // Compute the score only if the hit is competitive.
                    float score = scorer.GetScore();

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    comparer.Copy(bottom.Slot, doc);
                    UpdateBottom(doc, score);
                    comparer.SetBottom(bottom.Slot);
                }
                else
                {
                    // Compute the score only if the hit is competitive.
                    float score = scorer.GetScore();

                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    comparer.Copy(slot, doc);
                    Add(slot, doc, score);
                    if (queueFull)
                    {
                        comparer.SetBottom(bottom.Slot);
                    }
                }
            }

            public override void SetScorer(Scorer scorer)
            {
                this.scorer = scorer;
                comparer.SetScorer(scorer);
            }
        }

        /*
         * Implements a TopFieldCollector over one SortField criteria, while tracking
         * document scores but no maxScore, and assumes out of orderness in doc Ids
         * collection.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private class OutOfOrderOneComparerScoringNoMaxScoreCollector : OneComparerScoringNoMaxScoreCollector
        {
            public OutOfOrderOneComparerScoringNoMaxScoreCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
            }

            public override void Collect(int doc)
            {
                ++m_totalHits;
                if (queueFull)
                {
                    // Fastmatch: return if this hit is not competitive
                    int cmp = reverseMul * comparer.CompareBottom(doc);
                    if (cmp < 0 || (cmp == 0 && doc + docBase > bottom.Doc))
                    {
                        return;
                    }

                    // Compute the score only if the hit is competitive.
                    float score = scorer.GetScore();

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    comparer.Copy(bottom.Slot, doc);
                    UpdateBottom(doc, score);
                    comparer.SetBottom(bottom.Slot);
                }
                else
                {
                    // Compute the score only if the hit is competitive.
                    float score = scorer.GetScore();

                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    comparer.Copy(slot, doc);
                    Add(slot, doc, score);
                    if (queueFull)
                    {
                        comparer.SetBottom(bottom.Slot);
                    }
                }
            }

            public override bool AcceptsDocsOutOfOrder
            {
                get { return true; }
            }
        }

        /*
         * Implements a TopFieldCollector over one SortField criteria, with tracking
         * document scores and maxScore.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private class OneComparerScoringMaxScoreCollector : OneComparerNonScoringCollector
        {
            internal Scorer scorer;

            public OneComparerScoringMaxScoreCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
                // Must set maxScore to NEG_INF, or otherwise Math.max always returns NaN.
                maxScore = float.NegativeInfinity;
            }

            internal void UpdateBottom(int doc, float score)
            {
                bottom.Doc = docBase + doc;
                bottom.Score = score;
                bottom = m_pq.UpdateTop();
            }

            public override void Collect(int doc)
            {
                float score = scorer.GetScore();
                if (score > maxScore)
                {
                    maxScore = score;
                }
                ++m_totalHits;
                if (queueFull)
                {
                    if ((reverseMul * comparer.CompareBottom(doc)) <= 0)
                    {
                        // since docs are visited in doc Id order, if compare is 0, it means
                        // this document is largest than anything else in the queue, and
                        // therefore not competitive.
                        return;
                    }

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    comparer.Copy(bottom.Slot, doc);
                    UpdateBottom(doc, score);
                    comparer.SetBottom(bottom.Slot);
                }
                else
                {
                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    comparer.Copy(slot, doc);
                    Add(slot, doc, score);
                    if (queueFull)
                    {
                        comparer.SetBottom(bottom.Slot);
                    }
                }
            }

            public override void SetScorer(Scorer scorer)
            {
                this.scorer = scorer;
                base.SetScorer(scorer);
            }
        }

        /*
         * Implements a TopFieldCollector over one SortField criteria, with tracking
         * document scores and maxScore, and assumes out of orderness in doc Ids
         * collection.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private class OutOfOrderOneComparerScoringMaxScoreCollector : OneComparerScoringMaxScoreCollector
        {
            public OutOfOrderOneComparerScoringMaxScoreCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
            }

            public override void Collect(int doc)
            {
                float score = scorer.GetScore();
                if (score > maxScore)
                {
                    maxScore = score;
                }
                ++m_totalHits;
                if (queueFull)
                {
                    // Fastmatch: return if this hit is not competitive
                    int cmp = reverseMul * comparer.CompareBottom(doc);
                    if (cmp < 0 || (cmp == 0 && doc + docBase > bottom.Doc))
                    {
                        return;
                    }

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    comparer.Copy(bottom.Slot, doc);
                    UpdateBottom(doc, score);
                    comparer.SetBottom(bottom.Slot);
                }
                else
                {
                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    comparer.Copy(slot, doc);
                    Add(slot, doc, score);
                    if (queueFull)
                    {
                        comparer.SetBottom(bottom.Slot);
                    }
                }
            }

            public override bool AcceptsDocsOutOfOrder
            {
                get { return true; }
            }
        }

        /*
         * Implements a TopFieldCollector over multiple SortField criteria, without
         * tracking document scores and maxScore.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private class MultiComparerNonScoringCollector : TopFieldCollector
        {
            internal readonly FieldComparer[] comparers;
            internal readonly int[] reverseMul;
            internal readonly FieldValueHitQueue<Entry> queue;

            public MultiComparerNonScoringCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
                this.queue = queue;
                comparers = queue.Comparers;
                reverseMul = queue.ReverseMul;
            }

            internal void UpdateBottom(int doc)
            {
                // bottom.score is already set to Float.NaN in add().
                bottom.Doc = docBase + doc;
                bottom = m_pq.UpdateTop();
            }

            public override void Collect(int doc)
            {
                ++m_totalHits;
                if (queueFull)
                {
                    // Fastmatch: return if this hit is not competitive
                    for (int i = 0; ; i++)
                    {
                        int c = reverseMul[i] * comparers[i].CompareBottom(doc);
                        if (c < 0)
                        {
                            // Definitely not competitive.
                            return;
                        }
                        else if (c > 0)
                        {
                            // Definitely competitive.
                            break;
                        }
                        else if (i == comparers.Length - 1)
                        {
                            // Here c=0. If we're at the last comparer, this doc is not
                            // competitive, since docs are visited in doc Id order, which means
                            // this doc cannot compete with any other document in the queue.
                            return;
                        }
                    }

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(bottom.Slot, doc);
                    }

                    UpdateBottom(doc);

                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].SetBottom(bottom.Slot);
                    }
                }
                else
                {
                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(slot, doc);
                    }
                    Add(slot, doc, float.NaN);
                    if (queueFull)
                    {
                        for (int i = 0; i < comparers.Length; i++)
                        {
                            comparers[i].SetBottom(bottom.Slot);
                        }
                    }
                }
            }

            public override void SetNextReader(AtomicReaderContext context)
            {
                docBase = context.DocBase;
                for (int i = 0; i < comparers.Length; i++)
                {
                    queue.SetComparer(i, comparers[i].SetNextReader(context));
                }
            }

            public override void SetScorer(Scorer scorer)
            {
                // set the value on all comparers
                for (int i = 0; i < comparers.Length; i++)
                {
                    comparers[i].SetScorer(scorer);
                }
            }
        }

        /*
         * Implements a TopFieldCollector over multiple SortField criteria, without
         * tracking document scores and maxScore, and assumes out of orderness in doc
         * Ids collection.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private class OutOfOrderMultiComparerNonScoringCollector : MultiComparerNonScoringCollector
        {
            public OutOfOrderMultiComparerNonScoringCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
            }

            public override void Collect(int doc)
            {
                ++m_totalHits;
                if (queueFull)
                {
                    // Fastmatch: return if this hit is not competitive
                    for (int i = 0; ; i++)
                    {
                        int c = reverseMul[i] * comparers[i].CompareBottom(doc);
                        if (c < 0)
                        {
                            // Definitely not competitive.
                            return;
                        }
                        else if (c > 0)
                        {
                            // Definitely competitive.
                            break;
                        }
                        else if (i == comparers.Length - 1)
                        {
                            // this is the equals case.
                            if (doc + docBase > bottom.Doc)
                            {
                                // Definitely not competitive
                                return;
                            }
                            break;
                        }
                    }

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(bottom.Slot, doc);
                    }

                    UpdateBottom(doc);

                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].SetBottom(bottom.Slot);
                    }
                }
                else
                {
                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(slot, doc);
                    }
                    Add(slot, doc, float.NaN);
                    if (queueFull)
                    {
                        for (int i = 0; i < comparers.Length; i++)
                        {
                            comparers[i].SetBottom(bottom.Slot);
                        }
                    }
                }
            }

            public override bool AcceptsDocsOutOfOrder
            {
                get { return true; }
            }
        }

        /*
         * Implements a TopFieldCollector over multiple SortField criteria, with
         * tracking document scores and maxScore.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private class MultiComparerScoringMaxScoreCollector : MultiComparerNonScoringCollector
        {
            internal Scorer scorer;

            public MultiComparerScoringMaxScoreCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
                // Must set maxScore to NEG_INF, or otherwise Math.max always returns NaN.
                maxScore = float.NegativeInfinity;
            }

            internal void UpdateBottom(int doc, float score)
            {
                bottom.Doc = docBase + doc;
                bottom.Score = score;
                bottom = m_pq.UpdateTop();
            }

            public override void Collect(int doc)
            {
                float score = scorer.GetScore();
                if (score > maxScore)
                {
                    maxScore = score;
                }
                ++m_totalHits;
                if (queueFull)
                {
                    // Fastmatch: return if this hit is not competitive
                    for (int i = 0; ; i++)
                    {
                        int c = reverseMul[i] * comparers[i].CompareBottom(doc);
                        if (c < 0)
                        {
                            // Definitely not competitive.
                            return;
                        }
                        else if (c > 0)
                        {
                            // Definitely competitive.
                            break;
                        }
                        else if (i == comparers.Length - 1)
                        {
                            // Here c=0. If we're at the last comparer, this doc is not
                            // competitive, since docs are visited in doc Id order, which means
                            // this doc cannot compete with any other document in the queue.
                            return;
                        }
                    }

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(bottom.Slot, doc);
                    }

                    UpdateBottom(doc, score);

                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].SetBottom(bottom.Slot);
                    }
                }
                else
                {
                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(slot, doc);
                    }
                    Add(slot, doc, score);
                    if (queueFull)
                    {
                        for (int i = 0; i < comparers.Length; i++)
                        {
                            comparers[i].SetBottom(bottom.Slot);
                        }
                    }
                }
            }

            public override void SetScorer(Scorer scorer)
            {
                this.scorer = scorer;
                base.SetScorer(scorer);
            }
        }

        /*
         * Implements a TopFieldCollector over multiple SortField criteria, with
         * tracking document scores and maxScore, and assumes out of orderness in doc
         * Ids collection.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private sealed class OutOfOrderMultiComparerScoringMaxScoreCollector : MultiComparerScoringMaxScoreCollector
        {
            public OutOfOrderMultiComparerScoringMaxScoreCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
            }

            public override void Collect(int doc)
            {
                float score = scorer.GetScore();
                if (score > maxScore)
                {
                    maxScore = score;
                }
                ++m_totalHits;
                if (queueFull)
                {
                    // Fastmatch: return if this hit is not competitive
                    for (int i = 0; ; i++)
                    {
                        int c = reverseMul[i] * comparers[i].CompareBottom(doc);
                        if (c < 0)
                        {
                            // Definitely not competitive.
                            return;
                        }
                        else if (c > 0)
                        {
                            // Definitely competitive.
                            break;
                        }
                        else if (i == comparers.Length - 1)
                        {
                            // this is the equals case.
                            if (doc + docBase > bottom.Doc)
                            {
                                // Definitely not competitive
                                return;
                            }
                            break;
                        }
                    }

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(bottom.Slot, doc);
                    }

                    UpdateBottom(doc, score);

                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].SetBottom(bottom.Slot);
                    }
                }
                else
                {
                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(slot, doc);
                    }
                    Add(slot, doc, score);
                    if (queueFull)
                    {
                        for (int i = 0; i < comparers.Length; i++)
                        {
                            comparers[i].SetBottom(bottom.Slot);
                        }
                    }
                }
            }

            public override bool AcceptsDocsOutOfOrder
            {
                get { return true; }
            }
        }

        /*
         * Implements a TopFieldCollector over multiple SortField criteria, with
         * tracking document scores and maxScore.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private class MultiComparerScoringNoMaxScoreCollector : MultiComparerNonScoringCollector
        {
            internal Scorer scorer;

            public MultiComparerScoringNoMaxScoreCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
            }

            internal void UpdateBottom(int doc, float score)
            {
                bottom.Doc = docBase + doc;
                bottom.Score = score;
                bottom = m_pq.UpdateTop();
            }

            public override void Collect(int doc)
            {
                ++m_totalHits;
                if (queueFull)
                {
                    // Fastmatch: return if this hit is not competitive
                    for (int i = 0; ; i++)
                    {
                        int c = reverseMul[i] * comparers[i].CompareBottom(doc);
                        if (c < 0)
                        {
                            // Definitely not competitive.
                            return;
                        }
                        else if (c > 0)
                        {
                            // Definitely competitive.
                            break;
                        }
                        else if (i == comparers.Length - 1)
                        {
                            // Here c=0. If we're at the last comparer, this doc is not
                            // competitive, since docs are visited in doc Id order, which means
                            // this doc cannot compete with any other document in the queue.
                            return;
                        }
                    }

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(bottom.Slot, doc);
                    }

                    // Compute score only if it is competitive.
                    float score = scorer.GetScore();
                    UpdateBottom(doc, score);

                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].SetBottom(bottom.Slot);
                    }
                }
                else
                {
                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(slot, doc);
                    }

                    // Compute score only if it is competitive.
                    float score = scorer.GetScore();
                    Add(slot, doc, score);
                    if (queueFull)
                    {
                        for (int i = 0; i < comparers.Length; i++)
                        {
                            comparers[i].SetBottom(bottom.Slot);
                        }
                    }
                }
            }

            public override void SetScorer(Scorer scorer)
            {
                this.scorer = scorer;
                base.SetScorer(scorer);
            }
        }

        /*
         * Implements a TopFieldCollector over multiple SortField criteria, with
         * tracking document scores and maxScore, and assumes out of orderness in doc
         * Ids collection.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private sealed class OutOfOrderMultiComparerScoringNoMaxScoreCollector : MultiComparerScoringNoMaxScoreCollector
        {
            public OutOfOrderMultiComparerScoringNoMaxScoreCollector(FieldValueHitQueue<Entry> queue, int numHits, bool fillFields)
                : base(queue, numHits, fillFields)
            {
            }

            public override void Collect(int doc)
            {
                ++m_totalHits;
                if (queueFull)
                {
                    // Fastmatch: return if this hit is not competitive
                    for (int i = 0; ; i++)
                    {
                        int c = reverseMul[i] * comparers[i].CompareBottom(doc);
                        if (c < 0)
                        {
                            // Definitely not competitive.
                            return;
                        }
                        else if (c > 0)
                        {
                            // Definitely competitive.
                            break;
                        }
                        else if (i == comparers.Length - 1)
                        {
                            // this is the equals case.
                            if (doc + docBase > bottom.Doc)
                            {
                                // Definitely not competitive
                                return;
                            }
                            break;
                        }
                    }

                    // this hit is competitive - replace bottom element in queue & adjustTop
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(bottom.Slot, doc);
                    }

                    // Compute score only if it is competitive.
                    float score = scorer.GetScore();
                    UpdateBottom(doc, score);

                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].SetBottom(bottom.Slot);
                    }
                }
                else
                {
                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = m_totalHits - 1;
                    // Copy hit into queue
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(slot, doc);
                    }

                    // Compute score only if it is competitive.
                    float score = scorer.GetScore();
                    Add(slot, doc, score);
                    if (queueFull)
                    {
                        for (int i = 0; i < comparers.Length; i++)
                        {
                            comparers[i].SetBottom(bottom.Slot);
                        }
                    }
                }
            }

            public override void SetScorer(Scorer scorer)
            {
                this.scorer = scorer;
                base.SetScorer(scorer);
            }

            public override bool AcceptsDocsOutOfOrder
            {
                get { return true; }
            }
        }

        /*
         * Implements a TopFieldCollector when after != null.
         */
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        private sealed class PagingFieldCollector : TopFieldCollector
        {
            internal Scorer scorer;
            internal int collectedHits;
            internal readonly FieldComparer[] comparers;
            internal readonly int[] reverseMul;
            internal readonly FieldValueHitQueue<Entry> queue;
            internal readonly bool trackDocScores;
            internal readonly bool trackMaxScore;
            internal readonly FieldDoc after;
            internal int afterDoc;

            public PagingFieldCollector(FieldValueHitQueue<Entry> queue, FieldDoc after, int numHits, bool fillFields, bool trackDocScores, bool trackMaxScore)
                : base(queue, numHits, fillFields)
            {
                this.queue = queue;
                this.trackDocScores = trackDocScores;
                this.trackMaxScore = trackMaxScore;
                this.after = after;
                comparers = queue.Comparers;
                reverseMul = queue.ReverseMul;

                // Must set maxScore to NEG_INF, or otherwise Math.max always returns NaN.
                maxScore = float.NegativeInfinity;

                // Tell all comparers their top value:
                for (int i = 0; i < comparers.Length; i++)
                {
                    FieldComparer comparer = comparers[i];
                    comparer.SetTopValue(after.Fields[i]);
                }
            }

            internal void UpdateBottom(int doc, float score)
            {
                bottom.Doc = docBase + doc;
                bottom.Score = score;
                bottom = m_pq.UpdateTop();
            }

            public override void Collect(int doc)
            {
                //System.out.println("  collect doc=" + doc);

                m_totalHits++;

                float score = float.NaN;
                if (trackMaxScore)
                {
                    score = scorer.GetScore();
                    if (score > maxScore)
                    {
                        maxScore = score;
                    }
                }

                if (queueFull)
                {
                    // Fastmatch: return if this hit is no better than
                    // the worst hit currently in the queue:
                    for (int i = 0; ; i++)
                    {
                        int c = reverseMul[i] * comparers[i].CompareBottom(doc);
                        if (c < 0)
                        {
                            // Definitely not competitive.
                            return;
                        }
                        else if (c > 0)
                        {
                            // Definitely competitive.
                            break;
                        }
                        else if (i == comparers.Length - 1)
                        {
                            // this is the equals case.
                            if (doc + docBase > bottom.Doc)
                            {
                                // Definitely not competitive
                                return;
                            }
                            break;
                        }
                    }
                }

                // Check if this hit was already collected on a
                // previous page:
                bool sameValues = true;
                for (int compIDX = 0; compIDX < comparers.Length; compIDX++)
                {
                    FieldComparer comp = comparers[compIDX];

                    int cmp = reverseMul[compIDX] * comp.CompareTop(doc);
                    if (cmp > 0)
                    {
                        // Already collected on a previous page
                        //System.out.println("    skip: before");
                        return;
                    }
                    else if (cmp < 0)
                    {
                        // Not yet collected
                        sameValues = false;
                        //System.out.println("    keep: after; reverseMul=" + reverseMul[compIDX]);
                        break;
                    }
                }

                // Tie-break by docID:
                if (sameValues && doc <= afterDoc)
                {
                    // Already collected on a previous page
                    //System.out.println("    skip: tie-break");
                    return;
                }

                if (queueFull)
                {
                    // this hit is competitive - replace bottom element in queue & adjustTop
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(bottom.Slot, doc);
                    }

                    // Compute score only if it is competitive.
                    if (trackDocScores && !trackMaxScore)
                    {
                        score = scorer.GetScore();
                    }
                    UpdateBottom(doc, score);

                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].SetBottom(bottom.Slot);
                    }
                }
                else
                {
                    collectedHits++;

                    // Startup transient: queue hasn't gathered numHits yet
                    int slot = collectedHits - 1;
                    //System.out.println("    slot=" + slot);
                    // Copy hit into queue
                    for (int i = 0; i < comparers.Length; i++)
                    {
                        comparers[i].Copy(slot, doc);
                    }

                    // Compute score only if it is competitive.
                    if (trackDocScores && !trackMaxScore)
                    {
                        score = scorer.GetScore();
                    }
                    bottom = m_pq.Add(new Entry(slot, docBase + doc, score));
                    queueFull = collectedHits == numHits;
                    if (queueFull)
                    {
                        for (int i = 0; i < comparers.Length; i++)
                        {
                            comparers[i].SetBottom(bottom.Slot);
                        }
                    }
                }
            }

            public override void SetScorer(Scorer scorer)
            {
                this.scorer = scorer;
                for (int i = 0; i < comparers.Length; i++)
                {
                    comparers[i].SetScorer(scorer);
                }
            }

            public override bool AcceptsDocsOutOfOrder
            {
                get { return true; }
            }

            public override void SetNextReader(AtomicReaderContext context)
            {
                docBase = context.DocBase;
                afterDoc = after.Doc - docBase;
                for (int i = 0; i < comparers.Length; i++)
                {
                    queue.SetComparer(i, comparers[i].SetNextReader(context));
                }
            }
        }

        private static readonly ScoreDoc[] EMPTY_SCOREDOCS = new ScoreDoc[0];

        private readonly bool fillFields;

        /// <summary>
        /// Stores the maximum score value encountered, needed for normalizing. If
        /// document scores are not tracked, this value is initialized to NaN.
        /// </summary>
        internal float maxScore = float.NaN;

        internal readonly int numHits;
        internal FieldValueHitQueue.Entry bottom = null;
        internal bool queueFull;
        internal int docBase;

        // Declaring the constructor private prevents extending this class by anyone
        // else. Note that the class cannot be final since it's extended by the
        // internal versions. If someone will define a constructor with any other
        // visibility, then anyone will be able to extend the class, which is not what
        // we want.
        private TopFieldCollector(PriorityQueue<Entry> pq, int numHits, bool fillFields)
            : base(pq)
        {
            this.numHits = numHits;
            this.fillFields = fillFields;
        }

        /// <summary>
        /// Creates a new <seealso cref="TopFieldCollector"/> from the given
        /// arguments.
        ///
        /// <p><b>NOTE</b>: The instances returned by this method
        /// pre-allocate a full array of length
        /// <code>numHits</code>.
        /// </summary>
        /// <param name="sort">
        ///          the sort criteria (SortFields). </param>
        /// <param name="numHits">
        ///          the number of results to collect. </param>
        /// <param name="fillFields">
        ///          specifies whether the actual field values should be returned on
        ///          the results (FieldDoc). </param>
        /// <param name="trackDocScores">
        ///          specifies whether document scores should be tracked and set on the
        ///          results. Note that if set to false, then the results' scores will
        ///          be set to Float.NaN. Setting this to true affects performance, as
        ///          it incurs the score computation on each competitive result.
        ///          Therefore if document scores are not required by the application,
        ///          it is recommended to set it to false. </param>
        /// <param name="trackMaxScore">
        ///          specifies whether the query's maxScore should be tracked and set
        ///          on the resulting <seealso cref="TopDocs"/>. Note that if set to false,
        ///          <seealso cref="TopDocs#getMaxScore()"/> returns Float.NaN. Setting this to
        ///          true affects performance as it incurs the score computation on
        ///          each result. Also, setting this true automatically sets
        ///          <code>trackDocScores</code> to true as well. </param>
        /// <param name="docsScoredInOrder">
        ///          specifies whether documents are scored in doc Id order or not by
        ///          the given <seealso cref="Scorer"/> in <seealso cref="#setScorer(Scorer)"/>. </param>
        /// <returns> a <seealso cref="TopFieldCollector"/> instance which will sort the results by
        ///         the sort criteria. </returns>
        /// <exception cref="IOException"> if there is a low-level I/O error </exception>
        public static TopFieldCollector Create(Sort sort, int numHits, bool fillFields, bool trackDocScores, bool trackMaxScore, bool docsScoredInOrder)
        {
            return Create(sort, numHits, null, fillFields, trackDocScores, trackMaxScore, docsScoredInOrder);
        }

        /// <summary>
        /// Creates a new <seealso cref="TopFieldCollector"/> from the given
        /// arguments.
        ///
        /// <p><b>NOTE</b>: The instances returned by this method
        /// pre-allocate a full array of length
        /// <code>numHits</code>.
        /// </summary>
        /// <param name="sort">
        ///          the sort criteria (SortFields). </param>
        /// <param name="numHits">
        ///          the number of results to collect. </param>
        /// <param name="after">
        ///          only hits after this FieldDoc will be collected </param>
        /// <param name="fillFields">
        ///          specifies whether the actual field values should be returned on
        ///          the results (FieldDoc). </param>
        /// <param name="trackDocScores">
        ///          specifies whether document scores should be tracked and set on the
        ///          results. Note that if set to false, then the results' scores will
        ///          be set to Float.NaN. Setting this to true affects performance, as
        ///          it incurs the score computation on each competitive result.
        ///          Therefore if document scores are not required by the application,
        ///          it is recommended to set it to false. </param>
        /// <param name="trackMaxScore">
        ///          specifies whether the query's maxScore should be tracked and set
        ///          on the resulting <seealso cref="TopDocs"/>. Note that if set to false,
        ///          <seealso cref="TopDocs#getMaxScore()"/> returns Float.NaN. Setting this to
        ///          true affects performance as it incurs the score computation on
        ///          each result. Also, setting this true automatically sets
        ///          <code>trackDocScores</code> to true as well. </param>
        /// <param name="docsScoredInOrder">
        ///          specifies whether documents are scored in doc Id order or not by
        ///          the given <seealso cref="Scorer"/> in <seealso cref="#setScorer(Scorer)"/>. </param>
        /// <returns> a <seealso cref="TopFieldCollector"/> instance which will sort the results by
        ///         the sort criteria. </returns>
        /// <exception cref="IOException"> if there is a low-level I/O error </exception>
        public static TopFieldCollector Create(Sort sort, int numHits, FieldDoc after, bool fillFields, bool trackDocScores, bool trackMaxScore, bool docsScoredInOrder)
        {
            if (sort.fields.Length == 0)
            {
                throw new System.ArgumentException("Sort must contain at least one field");
            }

            if (numHits <= 0)
            {
                throw new System.ArgumentException("numHits must be > 0; please use TotalHitCountCollector if you just need the total hit count");
            }

            FieldValueHitQueue<Entry> queue = FieldValueHitQueue.Create<Entry>(sort.fields, numHits);

            if (after == null)
            {
                if (queue.Comparers.Length == 1)
                {
                    if (docsScoredInOrder)
                    {
                        if (trackMaxScore)
                        {
                            return new OneComparerScoringMaxScoreCollector(queue, numHits, fillFields);
                        }
                        else if (trackDocScores)
                        {
                            return new OneComparerScoringNoMaxScoreCollector(queue, numHits, fillFields);
                        }
                        else
                        {
                            return new OneComparerNonScoringCollector(queue, numHits, fillFields);
                        }
                    }
                    else
                    {
                        if (trackMaxScore)
                        {
                            return new OutOfOrderOneComparerScoringMaxScoreCollector(queue, numHits, fillFields);
                        }
                        else if (trackDocScores)
                        {
                            return new OutOfOrderOneComparerScoringNoMaxScoreCollector(queue, numHits, fillFields);
                        }
                        else
                        {
                            return new OutOfOrderOneComparerNonScoringCollector(queue, numHits, fillFields);
                        }
                    }
                }

                // multiple comparers.
                if (docsScoredInOrder)
                {
                    if (trackMaxScore)
                    {
                        return new MultiComparerScoringMaxScoreCollector(queue, numHits, fillFields);
                    }
                    else if (trackDocScores)
                    {
                        return new MultiComparerScoringNoMaxScoreCollector(queue, numHits, fillFields);
                    }
                    else
                    {
                        return new MultiComparerNonScoringCollector(queue, numHits, fillFields);
                    }
                }
                else
                {
                    if (trackMaxScore)
                    {
                        return new OutOfOrderMultiComparerScoringMaxScoreCollector(queue, numHits, fillFields);
                    }
                    else if (trackDocScores)
                    {
                        return new OutOfOrderMultiComparerScoringNoMaxScoreCollector(queue, numHits, fillFields);
                    }
                    else
                    {
                        return new OutOfOrderMultiComparerNonScoringCollector(queue, numHits, fillFields);
                    }
                }
            }
            else
            {
                if (after.Fields == null)
                {
                    throw new System.ArgumentException("after.fields wasn't set; you must pass fillFields=true for the previous search");
                }

                if (after.Fields.Length != sort.GetSort().Length)
                {
                    throw new System.ArgumentException("after.fields has " + after.Fields.Length + " values but sort has " + sort.GetSort().Length);
                }

                return new PagingFieldCollector(queue, after, numHits, fillFields, trackDocScores, trackMaxScore);
            }
        }

        internal void Add(int slot, int doc, float score)
        {
            bottom = m_pq.Add(new Entry(slot, docBase + doc, score));
            queueFull = m_totalHits == numHits;
        }

        /*
         * Only the following callback methods need to be overridden since
         * topDocs(int, int) calls them to return the results.
         */

        protected override void PopulateResults(ScoreDoc[] results, int howMany)
        {
            if (fillFields)
            {
                // avoid casting if unnecessary.
                FieldValueHitQueue<Entry> queue = (FieldValueHitQueue<Entry>)m_pq;
                for (int i = howMany - 1; i >= 0; i--)
                {
                    results[i] = queue.FillFields(queue.Pop());
                }
            }
            else
            {
                for (int i = howMany - 1; i >= 0; i--)
                {
                    Entry entry = m_pq.Pop();
                    results[i] = new FieldDoc(entry.Doc, entry.Score);
                }
            }
        }

        protected override TopDocs NewTopDocs(ScoreDoc[] results, int start)
        {
            if (results == null)
            {
                results = EMPTY_SCOREDOCS;
                // Set maxScore to NaN, in case this is a maxScore tracking collector.
                maxScore = float.NaN;
            }

            // If this is a maxScoring tracking collector and there were no results,
            return new TopFieldDocs(m_totalHits, results, ((FieldValueHitQueue<Entry>)m_pq).Fields, maxScore);
        }

        public override bool AcceptsDocsOutOfOrder
        {
            get { return false; }
        }
    }
}