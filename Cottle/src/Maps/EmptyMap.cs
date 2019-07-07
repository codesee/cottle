﻿using System.Collections.Generic;

namespace Cottle.Maps
{
    internal class EmptyMap : AbstractMap
    {
        #region Attributes

        private static readonly IList<KeyValuePair<Value, Value>> Pairs = new KeyValuePair<Value, Value>[0];

        #endregion

        #region Properties / Instance

        public override int Count => 0;

        #endregion

        #region Properties / Static

        public static EmptyMap Instance { get; } = new EmptyMap();

        #endregion

        #region Methods

        public override bool Contains(Value key)
        {
            return false;
        }

        public override bool TryGet(Value key, out Value value)
        {
            value = default;

            return false;
        }

        public override IEnumerator<KeyValuePair<Value, Value>> GetEnumerator()
        {
            return EmptyMap.Pairs.GetEnumerator();
        }

        public override int GetHashCode()
        {
            return 0;
        }

        #endregion
    }
}