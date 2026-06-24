namespace GameClient.Misc
{
    /// <summary>
    /// Single source of truth for the second-argument bool on
    /// <c>Serializer.ConvertObjectToBytes(obj, bool)</c> and
    /// <c>Serializer.ConvertBytesToObject&lt;T&gt;(bytes, bool)</c> used in
    /// Synchronous (online-visit) packet handlers.
    ///
    /// The bool's exact semantic in RTShared's Serializer is not confirmed
    /// from source — we believe it toggles MessagePack compression based on
    /// strings observed in RTShared.dll, but the receiver-side mismatch in
    /// the legacy code (PM_SGameSpeed) means flipping it without a paired
    /// experiment is risky.
    ///
    /// Sender and receiver MUST use the same value for every Synchronous
    /// packet pair, or deserialization will fail.
    /// </summary>
    public static class SynchronousOptions
    {
        public const bool CompressContents = false;
    }
}
