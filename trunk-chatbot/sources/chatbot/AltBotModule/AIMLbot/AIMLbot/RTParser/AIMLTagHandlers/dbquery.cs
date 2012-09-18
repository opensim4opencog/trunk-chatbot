using System;
using System.Runtime;
using System.Text;
using System.Xml;
using System.Collections;
using System.Collections.Generic;
using System.IO;
//using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using AltAIMLbot;
using AltAIMLbot.Utils;
using AltAIMLParser;
using RTParser;
using RTParser.Utils;
using Lucene.Net.Store;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Documents;
using Lucene.Net.Search;
using Lucene.Net.QueryParsers;
using MushDLR223.ScriptEngines;
using MushDLR223.Utilities;
using MushDLR223.Virtualization;

namespace RTParser.AIMLTagHandlers
{

    /// <summary>
    /// 
    /// </summary>
    public class dbquery : RTParser.Utils.AIMLTagHandler
    {

        public dbquery(RTParser.AltBot bot,
                User user,
                SubQuery query,
                Request request,
                Result result,
                XmlNode templateNode)
            : base(bot, user, query, request, result, templateNode)
        {
        }


        protected override Unifiable ProcessChange()
        {
            if (CheckNode("dbquery"))
            {
                if (templateNode.ChildNodes.Count > 0 && templateNode.FirstChild.Name != "li")
                    return ProcessChangeLegacy();
                bool hasPassed = false;
                string majorPassed = null;
                foreach (var node in templateNode)
                {
                    // otherwise take the tag content as a srai (to trip say a random reply)
                    const bool expandOnNoHits = true; // actually WordNet
                    const float threshold = 0.0f;
                    Unifiable templateNodeInnerValue = ProcessChildNode(((XmlNode)node));
                    string failPrefix = AltBot.GetAttribValue(((XmlNode)node), "failprefix", "").ToLower();
                    string passPrefix = AltBot.GetAttribValue(((XmlNode)node), "passprefix", "").ToLower();
                    string resultPrefix = AltBot.GetAttribValue(((XmlNode)node), "resultprefix", "").ToLower();
                    if (!string.IsNullOrEmpty(failPrefix))
                    {
                        //on <dbquery> failure, use a <srai> fallback
                        string sariCallStr = failPrefix + " " + (string) templateNodeInnerValue;
                        return callSRAI(sariCallStr);
                    }
                    if (!string.IsNullOrEmpty(passPrefix))
                    {
                        //on <dbquery> failure, use a <srai> fallback
                        majorPassed = passPrefix + " " + (string)templateNodeInnerValue;
                    }
                    if (IsNullOrEmpty(templateNodeInnerValue)) continue;
                    string searchTerm1 = TargetBot.LuceneIndexer.FixPronouns(templateNodeInnerValue,
                                                                             request.Requester.grabSettingNoDebug);
                    if (TargetBot.LuceneIndexer.MayAsk(searchTerm1, ((XmlNode)node)) == null)
                    {
                        writeToLogWarn("WARNING: NO DBASK " + searchTerm1);
                        continue;
                    }
                    float reliability;
                    Unifiable converseMemo = TargetBot.LuceneIndexer.AskQuery(
                        searchTerm1,
                        this.writeToLog,
                        () =>
                        {
                            if (string.IsNullOrEmpty(failPrefix)) return null;
                            //on <dbquery> failure, use a <srai> fallback
                            string sariCallStr = failPrefix + " " + (string)templateNodeInnerValue;
                            return callSRAI(sariCallStr);
                        },
                        this.templateNode,
                        threshold,
                        true, // use Wordnet
                        expandOnNoHits, out reliability);
                    if (!IsNullOrEmpty(converseMemo))
                    {
                        hasPassed = true;
                        QueryHasSuceeded = true;

                        if (!string.IsNullOrEmpty(passPrefix))
                        {
                            //on <dbquery> pass, use a <srai> for success
                            break;
                        }
                        if (!string.IsNullOrEmpty(resultPrefix))
                        {
                            //on <dbquery> failure, use a <srai> fallback
                            string sariCallStr = resultPrefix + " " + (string)converseMemo;
                            return callSRAI(sariCallStr);
                        }
                        return converseMemo;
                        //Unifiable converseMemo = Proc.conversationStack.Pop();
                    }
                }
                if (hasPassed)
                {
                    return callSRAI(majorPassed);
                }
                // if there is a high enough scoring record in Lucene, use up to max number of them?
                // otherwise there is a conversation memo then pop it??
            }
            return Unifiable.Empty;

        }

        protected Unifiable ProcessChangeLegacy()
        {
            if (CheckNode("dbquery"))
            {               
                // otherwise take the tag content as a srai (to trip say a random reply)
                const bool expandOnNoHits = true; // actually WordNet
                const float threshold = 0.0f;
                Unifiable templateNodeInnerValue = Recurse();
                string searchTermOrig = (string)templateNodeInnerValue;
                string searchTerm1 = TargetBot.LuceneIndexer.FixPronouns(searchTermOrig, request.Requester.grabSettingNoDebug);
                if (TargetBot.LuceneIndexer.MayAsk(searchTerm1, templateNode) == null)
                {
                    writeToLogWarn("WARNING: NO DBASK " + searchTerm1);
                    QueryHasFailed = true;
                    return FAIL;
                }
                float reliability;
                string failPrefix = AltBot.GetAttribValue(templateNode, "failprefix", "").ToLower();
                Unifiable converseMemo = TargetBot.LuceneIndexer.AskQuery(searchTerm1, this.writeToLog,
                                                                          () =>
                                                                              {
                                                                                  //on <dbquery> failure, use a <srai> fallback
                                                                                  string sariCallStr = failPrefix + " " + request.rawInput;
                                                                                  return callSRAI(sariCallStr);

                                                                              },
                                                                             this.templateNode, 
                                                                             threshold, 
                                                                             true, // use Wordnet
                                                                             expandOnNoHits, out reliability);

                // if there is a high enough scoring record in Lucene, use up to max number of them?
                // otherwise there is a conversation memo then pop it??
                if (IsNullOrEmpty(converseMemo))
                {
                    //Unifiable converseMemo = Proc.conversationStack.Pop();
                }
                return converseMemo;
            }
            return Unifiable.Empty;

        }



        public override void writeToLog(string s, params object[] p)
        {
            //Proc.writeToLog("DBQUERY: " + s, p);
            //bool tempB = Proc.IsLogging;
            //Proc.IsLogging = true;
            // base.user.rbot.writeToLog("DBQUERY: " + s, p);
            //Proc.IsLogging = tempB;
            DLRConsole.DebugWriteLine("DBQUERY: " + s, p);

        }

    }
}