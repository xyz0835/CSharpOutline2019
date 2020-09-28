namespace CSharpOutline2019
{
    internal static class ClassificationName
    {
        /// <summary>
        /// 标点符号 比如 { } ( )
        /// </summary> 
        internal const string Punctuation = "punctuation";

        /// <summary>
        /// 注释
        /// </summary>
        internal const string Comment = "comment";

        /// <summary>
        /// 关键字
        /// </summary>
        internal const string Keyword = "keyword";

        /// <summary>
        /// 命名空间
        /// </summary>
        internal const string NamespaceName = "namespace name";

        /// <summary>
        /// 方法名
        /// </summary>
        internal const string MethodName = "method name";

        /// <summary>
        /// 类名
        /// </summary>
        internal const string ClassName = "class name";

        /// <summary>
        /// 接口名
        /// </summary>
        internal const string InterfaceName = "interface name";

        /// <summary>
        /// 结构名
        /// </summary>
        internal const string StructName = "struct name";

        /// <summary>
        /// 枚举名
        /// </summary>
        internal const string EnumName = "enum name";

        /// <summary>
        /// 委托名
        /// </summary>
        internal const string DelegateName = "delegate name";

        /// <summary>
        /// 变量名
        /// </summary>
        internal const string Name = "name";

        /// <summary>
        /// 操作符
        /// </summary>
        internal const string Operator = "operator";

        /// <summary>
        /// 标识符
        /// </summary>
        internal const string Identifier = "identifier";

        /// <summary>
        /// return break等
        /// </summary>
        internal const string KeywordControl = "keyword - control";

        internal const string XMLDocumentCommandDelimiter = "xml doc comment - delimiter";
        internal const string XMLDocumentCommandText = "xml doc comment - text";
        internal const string XMLDocumentCommandName = "xml doc comment - name";

        internal const string PreProcessorKeyword = "preprocessor keyword";
        internal const string PreProcessorText = "preprocessor text";

        /// <summary>
        /// 注释
        /// </summary>
        /// <param name="classfication"></param>
        /// <returns></returns>
        public static bool IsComment(string classfication)
        {
            return classfication == Comment || classfication.StartsWith("xml doc comment"); //
        }

        /// <summary>
        /// 预处理命令 #region #if  #endif #else 等
        /// </summary>
        /// <param name="classfication"></param>
        /// <returns></returns>
        public static bool IsProcessor(string classfication)
        {
            return classfication == PreProcessorKeyword;
        }
    }
}
