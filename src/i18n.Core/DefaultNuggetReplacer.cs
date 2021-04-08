using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using i18n.Core.Abstractions;
using i18n.Core.Helpers;
using i18n.Core.Pot.Helpers;
using JetBrains.Annotations;

namespace i18n.Core
{
    public interface INuggetReplacer
    {
        string Replace([NotNull] CultureDictionary cultureDictionary, string text);
    }

    public class DefaultNuggetReplacer : INuggetReplacer
    {
        static readonly Regex NuggetRegex;
        static readonly Regex RegexPrintfIdentifiers;

        // https://github.com/turquoiseowl/i18n/blob/ce7bdc9d8a8b92022c42417edeff4fb9ce8d3170/src/i18n.Domain/Helpers/NuggetParser.cs#L149
        const NuggetParser.Context context = NuggetParser.Context.ResponseProcessing;

        static DefaultNuggetReplacer()
        {
            var nuggetTokens = new NuggetTokens("[[[", "]]]", "|||", "///");

            const RegexOptions regexOptions = RegexOptions.CultureInvariant
                                              | RegexOptions.Singleline
                                              | RegexOptions.Compiled;

            // Prep the regexes. We escape each token char to ensure it is not misinterpreted.
            // · Breakdown e.g. "\[\[\[(.+?)(?:\|\|\|(.+?))*(?:\/\/\/(.+?))?\]\]\]"
            NuggetRegex = new Regex(
                string.Format(@"{0}(.+?)(?:{1}(.{4}?))*(?:{2}(.+?))?{3}",
                    EscapeString(nuggetTokens.BeginToken),
                    EscapeString(nuggetTokens.DelimiterToken),
                    EscapeString(nuggetTokens.CommentToken),
                    EscapeString(nuggetTokens.EndToken),
                    // ReSharper disable once UnreachableCode
                    context == NuggetParser.Context.SourceProcessing ? "+" : "*"), regexOptions);


            RegexPrintfIdentifiers = new Regex(
               @"(%\d+)",
               RegexOptions.CultureInvariant);
        }

        public string Replace(CultureDictionary cultureDictionary, string text)
        {
            if (cultureDictionary == null) throw new ArgumentNullException(nameof(cultureDictionary));

            string ReplaceNuggets(Match match)
            {
                var nugget = new Nugget(match, context);

                var message = cultureDictionary[nugget.MsgId] ?? nugget.MsgId;

                if (nugget.IsFormatted)
                {
                    // Convert any identifies in a formatted nugget: %0 -> {0}
                    message = ConvertIdentifiersInMsgId(message);
                    // Format the message.
                    var formatItems = new List<string>(nugget.FormatItems);
                    try
                    {
                        // not supported for now
                        // translate nuggets in parameters
                        //for (int i = 0; i < formatItems.Count; i++)
                        //{
                        //    // if formatItem (parameter) is null or does not contain NuggetParameterBegintoken then continue
                        //    if (formatItems[i] == null || !formatItems[i].Contains(_settings.NuggetParameterBeginToken)) continue;

                        //    // replace parameter tokens with nugget tokens 
                        //    var fItem = formatItems[i].Replace(_settings.NuggetParameterBeginToken, _settings.NuggetBeginToken).Replace(_settings.NuggetParameterEndToken, _settings.NuggetEndToken);
                        //    // and process nugget 
                        //    formatItems[i] = ProcessNuggets(fItem, languages);
                        //}

                        message = string.Format(message, formatItems.ToArray());
                    }
                    catch (FormatException /*e*/)
                    {
                        //message += string.Format(" [FORMAT EXCEPTION: {0}]", e.Message);
                        message += "[FORMAT EXCEPTION]";
                    }
                }

                return message;
            }

            return NuggetRegex.Replace(text, ReplaceNuggets);
        }

        /// <summary>
        /// Helper for converting the C printf-style %0, %1 ... style identifiers in a formatted nugget msgid string
        /// to the .NET-style format items: {0}, {1} ...
        /// </summary>
        /// <remarks>
        /// A formatted msgid may be in the form:
        /// <para>
        /// Enter between %1 and %0 characters
        /// </para>
        /// <para>
        /// For which we return:
        /// </para>
        /// <para>
        /// Enter between {1} and {0} characters
        /// </para>
        /// </remarks>
        static string ConvertIdentifiersInMsgId(string msgid)
        {
            // Convert %n style identifiers to {n} style.
            return RegexPrintfIdentifiers.Replace(msgid, delegate (Match match)
            {
                string s = match.Groups[1].Value;
                double id;
                if (ParseHelpers.TryParseDecimal(s, 1, s.Length - 1 + 1, out id))
                {
                    s = string.Format("{{{0}}}", id);
                }
                return s;
            });
        }

        static string EscapeString(string str, char escapeChar = '\\')
        {
            var str1 = new StringBuilder(str.Length * 2);
            foreach (var c in str)
            {
                str1.Append(escapeChar);
                str1.Append(c);
            }
            return str1.ToString();
        }

        readonly struct NuggetTokens
        {
            public string BeginToken { get; }
            public string EndToken { get; }
            public string DelimiterToken { get; }
            public string CommentToken { get; }

            public NuggetTokens(
                string beginToken,
                string endToken,
                string delimiterToken,
                string commentToken)
            {
                if (!beginToken.IsSet()) { throw new ArgumentNullException(nameof(beginToken)); }
                if (!endToken.IsSet()) { throw new ArgumentNullException(nameof(endToken)); }
                if (!delimiterToken.IsSet()) { throw new ArgumentNullException(nameof(delimiterToken)); }
                if (!commentToken.IsSet()) { throw new ArgumentNullException(nameof(commentToken)); }

                BeginToken = beginToken;
                EndToken = endToken;
                DelimiterToken = delimiterToken;
                CommentToken = commentToken;
            }
        }

        // Adapted from https://github.com/turquoiseowl/i18n/blob/b7c3e8fd87c04eac983051f6e7f7b1b00ed66df3/src/i18n.Domain/Helpers/NuggetParser.cs#L115
        private class Nugget
        {
            public string MsgId { get; set; }
            public string[] FormatItems { get; set; }
            public string Comment { get; set; }

            public Nugget(Match match, NuggetParser.Context context)
            {
                if (!match.Success
                    || match.Groups.Count != 4)
                {
                    throw new ArgumentException("Match requires 4 valid groups.", nameof(match));
                }
                // Extract msgid from 2nd capture group.
                this.MsgId = match.Groups[1].Value;
                // Extract format items from 3rd capture group.
                var formatItems = match.Groups[2].Captures;
                if (formatItems.Count != 0)
                {
                    this.FormatItems = new string[formatItems.Count];
                    int i = 0;
                    foreach (Capture capture in formatItems)
                    {
                        if (context == NuggetParser.Context.SourceProcessing
                            && !capture.Value.IsSet())
                        {
                            throw new ArgumentException("Only response processing context supported.", nameof(context));

                        } // bad format
                        this.FormatItems[i++] = capture.Value;
                    }
                }
                // Extract comment from 4th capture group.
                if (match.Groups[3].Value.IsSet())
                {
                    this.Comment = match.Groups[3].Value;
                }
            }

            // Helpers

            public bool IsFormatted
            {
                get
                {
                    return FormatItems != null && FormatItems.Length != 0;
                }
            }

            public override string ToString()
            {
                return MsgId;
            }

            public override bool Equals(object obj)
            {
                if (obj == null)
                {
                    return false;
                }
                if (this.GetType() != obj.GetType())
                {
                    return false;
                }
                Nugget other = (Nugget)obj;
                // Compare non-array members.
                if (MsgId != other.MsgId // NB: the operator==() on string objects handles null value on either side just fine.
                    || Comment != other.Comment)
                {
                    return false;
                }
                // Compare arrays.
                if ((FormatItems == null) != (other.FormatItems == null)
                    || (FormatItems != null && !FormatItems.SequenceEqual(other.FormatItems)))
                {
                    return false;
                }
                return true;
            }

            public override int GetHashCode()
            {
                return 0
                    .CombineHashCode(MsgId)
                    .CombineHashCode(FormatItems)
                    .CombineHashCode(Comment);
            }
        };
    }

}
