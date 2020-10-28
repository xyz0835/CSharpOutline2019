namespace CSharpOutline2019
{
    internal enum CodeRegionType
    {
        None,
        /// <summary>
        ///  { }
        /// </summary>
        Block,
        /// <summary>
        /// 注释
        /// </summary>
        Comment,
        /// <summary>
        /// #region #if #else #endif
        /// </summary>
        ProProcessor,
        /// <summary>
        /// using
        /// </summary>
        Using,
        /// <summary>
        ///  case default
        /// </summary>
        Switch,
    }
}
