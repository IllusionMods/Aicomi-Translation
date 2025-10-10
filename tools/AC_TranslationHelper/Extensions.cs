using System.Collections;

namespace AC_TranslationHelper
{
    internal static class Extensions
    {

        /// <summary>
        /// Turn anything with a GetEnumerator method to an IEnumerable with a casted type.
        /// Will throw on failure at start, or throw during enumeration if casting fails.
        /// </summary>
        public static IEnumerable<T> CastToEnumerable<T>(this object obj)
        {
            if (obj is IEnumerable<T> ie)
                return ie;

            return CastToEnumerable(obj).Cast<T>();
        }

        /// <summary>
        /// Turn anything with a GetEnumerator method to an IEnumerable.
        /// Will throw on failure at start.
        /// </summary>
        public static IEnumerable CastToEnumerable(this object obj)
        {
            if (obj is IEnumerable ie2)
                return ie2;

            return DynamicAsEnumerable(obj);

            IEnumerable DynamicAsEnumerable(object targetObj)
            {
                // Enumerate through reflection since mono version doesn't have dynamic keyword
                // In IL2CPP using foreach with dynamic targetObj throws cast exceptions because of IL2CPP types
                var mGetEnumerator = targetObj.GetType().GetMethod("GetEnumerator");
                if (mGetEnumerator == null) throw new ArgumentNullException(nameof(mGetEnumerator));
                var enumerator = mGetEnumerator.Invoke(targetObj, null);
                if (enumerator == null) throw new ArgumentNullException(nameof(enumerator));
                var enumeratorType = enumerator.GetType();
                var mMoveNext = enumeratorType.GetMethod("MoveNext");
                if (mMoveNext == null) throw new ArgumentNullException(nameof(mMoveNext));
                var mCurrent = enumeratorType.GetProperty("Current");
                if (mCurrent == null) throw new ArgumentNullException(nameof(mCurrent));
                while ((bool)mMoveNext.Invoke(enumerator, null))
                    yield return mCurrent.GetValue(enumerator, null);
            }
        }
    }
}
