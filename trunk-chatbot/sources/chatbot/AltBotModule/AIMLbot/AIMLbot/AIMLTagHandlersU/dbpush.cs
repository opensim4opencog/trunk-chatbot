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

namespace RTParser.AIMLTagHandlers
{
    public class dbpush : RTParser.Utils.AIMLTagHandlerU
    {

        public dbpush(RTParser.AltBot bot,
                User user,
                SubQuery query,
                Request request,
                Result result,
                XmlNode templateNode)
            : base(bot, user, query, request, result, templateNode)
        {
        }



        protected override Unifiable ProcessChangeU()
        {
            if (CheckNode("dbpush"))
            {
                // Simply push the filled in tag contents onto the stack
                try
                {
                    // what to remember
                    Unifiable templateNodeInnerValue = Recurse();
                    string myText0 = (string) templateNodeInnerValue;
                    var myText = TargetBot.LuceneIndexer.FixPronouns(myText0, request.Requester.grabSettingNoDebug);
                    writeToLog("FIXPRONOUNS: " + myText0 + " ->" + myText);
                    if (TargetBot.LuceneIndexer.MayPush(myText, templateNode) == null)
                    {
                        writeToLogWarn("WARNING: NO DBPUSH " + myText);
                        QueryHasFailed = true;
                        return FAIL;
                    }
                    AddSideEffect("DBPUSH " + myText,
                                  () => TargetBot.LuceneIndexer.InsertFactiod(myText, templateNode, null));
                    return "BEGINPUSH " + myText + " ENDPUSH";
                }
                catch (Exception e)
                {
                    writeToLog("ERROR: {0}", e);
                }

            }
            return Unifiable.Empty;

        }
    }




}