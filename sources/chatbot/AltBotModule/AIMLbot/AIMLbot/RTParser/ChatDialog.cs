using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
#if (COGBOT_LIBOMV || USE_STHREADS || true)
using System.Linq;
using AltAIMLbot.AIMLTagHandlers;
using AltAIMLbot.Database;
using AltAIMLbot.Normalize;
using AltAIMLbot.Variables;
using ThreadPoolUtil;
using Thread = ThreadPoolUtil.Thread;
using ThreadPool = ThreadPoolUtil.ThreadPool;
using Monitor = ThreadPoolUtil.Monitor;
#endif
using System.Threading;


using System.Xml;
using AIMLbot;
using AltAIMLbot;
using AltAIMLbot.Utils;
using AltAIMLParser;
using LAIR.ResourceAPIs.WordNet;
using MushDLR223.ScriptEngines;
using MushDLR223.Utilities;
using MushDLR223.Virtualization;
using UPath = AltAIMLbot.Unifiable;
using UList = System.Collections.Generic.List<AltAIMLbot.Utils.TemplateInfo>;
using PatternInfo = AltAIMLbot.Unifiable;
using ThatInfo = AltAIMLbot.Unifiable;
using TopicInfo = AltAIMLbot.Unifiable;
using GuardInfo = AltAIMLbot.Unifiable;
//using MatchState=RTParser.Utils.MatchState;
using ResponseInfo = AltAIMLbot.Unifiable;
using MasterRequest = AltAIMLbot.Utils.Request;


namespace AltAIMLbot
{
    interface IChatterBot
    {
        SystemExecHandler ChatWithHandler(string userName);
    }

    public partial class AltBot
    {
        public static bool BE_COMPLETE_NOT_FAST = false;
        public static int SraiDepthMax = 17;
        public bool AlwaysUseImmediateAimlInImput = true;
        public static bool RotateUsedTemplate = true;
        public bool DontUseSplitters = true;
        /*
        public readonly TaskQueueHandler HeardSelfSayQueue0 = new TaskQueueHandler("AIMLBot HeardSelf",
                                                                                   TimeSpan.FromMilliseconds(10),
                                                                                   TimeSpan.FromSeconds(10), true);*/
        public TaskQueueHandler HeardSelfSayQueue
        {
            get
            {
              //  if (BotAsUser == null) return HeardSelfSayQueue0;
                return BotAsUser.GetTaskQueueHandler("HeardSelfSay");
            }
        }
        private readonly Object chatTraceO = new object();
        public List<Thread> ThreadList { get { return HeardSelfSayQueue.InteruptableThreads; } }
        public bool chatTrace = true;
        private StreamWriter chatTraceW;
        private JoinedTextBuffer currentEar = new JoinedTextBuffer();
        private int streamDepth;
        //public Unifiable responderJustSaid;

        // last user talking to bot besides itself
        [ThreadStatic]
        private User _lastUser;
        internal User _slastUser;
        public User LastUser
        {
            get
            {
                if (_botAsUser != null)
                {
                    var LR = _botAsUser.LastResponder;
                    if (LR != null) return LR.Value;
                }
                if (IsInteractiveUser(_lastUser)) return _lastUser;
                //User LU = _lastResult != null ? _lastResult.Requester.Value : null;
                //if (IsInteractiveUser(LU)) return LU;
                return _lastUser ?? ExemplarUser ?? _slastUser;
                //return null;
            }
            set
            {
                if (_botAsUser != null)
                {
                    if (value == _botAsUser) return;
                    _botAsUser.LastResponder = value;
                }
                _slastUser = value ?? _lastUser ??  _slastUser;
                //User LU = LastUser;
                if (IsInteractiveUser(value))
                {
                    if (_behaviorContext == null)
                    {
                        BotBehaving = value.AsBehaviorUser();
                    }
                }
                if (!IsInteractiveUser(_lastUser) || IsInteractiveUser(value))
                {
                    _lastUser = value;
                    return;
                }
            }
        }

        //private Result _lastResult;
        public Result LastResult
        {
            get
            {
                var _lastResult = LastUser.GetResult(0, false);
                if (_lastResult != null)
                {
                    if (IsInteractiveUser(_lastResult.Requester)) return _lastResult;
                }
                else
                {
                    User LU = LastUser;
                    if (IsInteractiveUser(LU)) return LU.LastResult;
                }
                return _lastResult;
            }
            set
            {
                Result LR = LastResult;
                if (value == null) return;
                if (LR == null)
                {
                    LastUser.LastRequest.UsedResults.Add(value);
                }
                LastUser = value.Requester.Value;

                if (LR != null && LR.Requester != BotAsUser)
                {
                    if (value.Requester != LR.Requester)
                    {
                        _lastUser = (value.Requester ?? _lastUser).Value;
                    }
                }
            }
        }

        /// <summary>
        /// The directory to look in for the WordNet3 files
        /// </summary>
        public string PathToWordNet
        {
            get { return GetPathSetting("wordnetdirectory", "wordnet30"); }
        }

        /// <summary>
        /// The directory to look in for the Lucene Index files
        /// </summary>
        public string PathToLucene
        {
            get { return GetPathSetting("lucenedirectory", "lucenedb"); }
        }

        /// <summary>
        /// The number of categories this Proccessor has in its graphmaster "brain"
        /// </summary>
        public int Size
        {
            get { return DefaultStartGraph.Size + DefaultHeardSelfSayGraph.Size; }
        }

        private GraphMaster _g;
        private GraphMaster _h;
        private static GraphMaster TheUserListenerGraph;

        /// <summary>
        /// The "brain" of the Proccessor
        /// </summary>
        public GraphMaster DefaultStartGraph
        {
            get
            {
                if (_g != null) return _g;
                var NamePath = this.NamePath;
                NamePath = "default";
                if (String.IsNullOrEmpty(NamePath))
                {
                    writeToLog("ERROR NoName = No graphmapster!");
                    return null;
                }
                return GetGraph(NamePath, _g);
            }
        }
        public GraphMaster DefaultEventGraph
        {
            get
            {
                return DefaultStartGraph;
            }
        }
        public GraphMaster DefaultHeardYouSayGraph
        {
            get
            {
                if (TheUserListenerGraph != null) return TheUserListenerGraph;
                return GraphMaster.FindOrCreate("heardyousay", this);
            }
        }
        public GraphMaster DefaultHeardSelfSayGraph
        {
            get
            {
                if (_h != null) return _h;
                return GraphMaster.FindOrCreate("heardselfsay", this);
            }
        }


        /// <summary>
        /// The Markovian "brain" of the Proccessor for generation
        /// </summary>
        public MBrain MBrain
        {
            get { return mbrain; }
        }

        private readonly MBrain mbrain = new MBrain();

        public MBrain STM_Brain
        {
            get { return stm_brain; }
        }

        private readonly MBrain stm_brain = new MBrain();

        /// <summary>
        /// Proccessor for phonetic HMM
        /// </summary>
        // emission and transition stored as double hash tables
        public PhoneticHmm pHMM
        {
            get { return phoneHMM; }
        }

        private readonly PhoneticHmm phoneHMM = new PhoneticHmm();

        /// <summary>
        /// Proccessor for action Markov State Machine
        /// </summary>
        // emission and transition stored as double hash tables
        public actMSM pMSM
        {
            get { return botMSM; }
        }

        private readonly actMSM botMSM = new actMSM();
        //public string lastDefMSM;
        //public string lastDefState;

        static public Hashtable _wordAttributeHash = new Hashtable();
        public Hashtable wordAttributeHash
        {
            get { return _wordAttributeHash; }
        }


        // = new WordNetEngine(HostSystem.Combine(Environment.CurrentDirectory, this.GlobalSettings.grabSetting("wordnetdirectory")), true);

        //public string indexDir = @"C:\dev\Lucene\";
        public string fieldName = "TEXT_MATTER";
        public IEnglishFactiodEngine LuceneIndexer;
        public ITripleStore TripleStore;


        #region Conversation methods

        public Result SaidUser(string input, string user, string[] otherNameWhomInputAdresses, OutputDelegate traceConsole, bool saveResults, bool saveResultsOnJustHeard, bool isToplevel, RequestKind requestType)
        {
            Logger.Warn("In TODO code");
            User CurrentUser = GetCurrentUser(user);
            string otherUserName = otherNameWhomInputAdresses == null ? null : otherNameWhomInputAdresses[0];
            User targetUser = GetTargetUser(otherUserName, input, BotAsUser);
            var request = CurrentUser.CreateRequest(input, targetUser.JustSaid, targetUser, isToplevel, requestType);
            request.IsTraced = true;
            request.OriginalSalientRequest = request;
            request.SaveResultsOnJustHeard = saveResultsOnJustHeard;
            return GlobalChatWithUser(request, input, user, otherUserName, traceConsole, saveResults,
                                      saveResultsOnJustHeard, isToplevel, requestType);
        }

        internal User GetTargetUser(string otherNameWhomInputAdresses, string input, User fallback)
        {
            User targetUser = fallback ?? BotAsUser;
            string youser = input;

            int lastIndex = input.IndexOfAny("-,:".ToCharArray());
            User targ = null;
            if (lastIndex > 0)
            {
                youser = input.Substring(0, lastIndex);
                if (!youser.Contains("<"))
                {
                    targ = FindUser(youser);
                    if (targ != null) targetUser = targ;
                }
            }
            if (otherNameWhomInputAdresses != null)
                if (targ == null)
                {
                    targ = FindUser(otherNameWhomInputAdresses);
                    if (targ != null) targetUser = targ;
                }
            return targetUser;
        }

        public User GetCurrentUser(string user)
        {
            User CurrentUser = null;
            if (user != null)
            {

                User lckleyUser = null;
                if (user == "null")
                {
                    CurrentUser = this.LastUser;
                }
                else
                {
                    CurrentUser = FindUser0(user);
                    CurrentUser = CurrentUser ?? FindOrCreateUser(user) ?? this.LastUser;
                }
            }
            else
            {
                CurrentUser = this.LastUser;
            }
            return CurrentUser;
        }
        internal Result GlobalChatWithUser(Request request, string input, string speakerName, string listenerNameWhomInputAdresses, 
            OutputDelegate traceConsole, bool saveLastUserAndResult, bool saveResultsOnConverstion, bool isToplevel, RequestKind requestType)
        {
            Logger.Warn("In TODO code");
            traceConsole = traceConsole ?? writeDebugLine;
            User CurrentUser = GetCurrentUser(speakerName);
            var varMSM = this.pMSM;
            varMSM.clearEvidence();
            varMSM.clearNextStateValues();
            // myUser.TopicSetting = "collectevidencepatterns";
            Result res = null;
            User targetUser = GetTargetUser(listenerNameWhomInputAdresses, input, BotAsUser);
            request = request ??
                      CurrentUser.CreateRequest(input, targetUser.JustSaid, targetUser, isToplevel, requestType);
            request.IsTraced = true;
            request.OriginalSalientRequest = request;
            request.SaveResultsOnJustHeard = saveResultsOnConverstion;
            Result requestCurrentResult = request.FindOrCreateCurrentResult();
            ChatLabel label = request.PushScope;
            try
            {
                res = ChatWithRequest(request, requestCurrentResult, isToplevel, requestType, saveResultsOnConverstion);
            }
            catch (ChatSignal e)
            {
                if (label.IsSignal(e)) return e.result;
                throw;
            }
            catch (Exception exception)
            {
                traceConsole("" + exception);
            }
            finally
            {
                label.PopScope();
            }
            res = res ?? requestCurrentResult;
            string useOut = null;
            //string useOut = resOutput;
            if (!res.IsEmpty)
            {
                useOut = res.Output;
                CurrentUser = res.Requester.Value;
                string oTest = ToEnglish(useOut);
                if (oTest != null && oTest.Length > 2)
                {
                    useOut = oTest;
                }
            }
            if (string.IsNullOrEmpty(useOut))
            {
                useOut = "Interesting.";
                res.TemplateRating = Math.Max(res.Score, 0.5d);
                saveLastUserAndResult = false;
            }
            if (saveLastUserAndResult)
            {
                this.LastUser = CurrentUser;
                LastResult = res;
            }

            traceConsole(useOut);
            return res;
        }

        public MasterRequest MakeRequestToBot(Unifiable rawInput, string username, bool isToplevel, RequestKind requestType)
        {
            return MakeRequestToBot(rawInput, FindOrCreateUser(username), isToplevel, requestType);
        }

        public MasterRequest MakeRequestToBot(Unifiable rawInput, User findOrCreateUser, bool isToplevel, RequestKind requestType)
        {
            Logger.Warn("In TODO code");
            var rtarget = BotAsUser;
            Unifiable botLastSaid = findOrCreateUser.That;
            MasterRequest r = findOrCreateUser.CreateRequest(rawInput, botLastSaid, rtarget, isToplevel, requestType);
            if (rtarget == null)
            {
                OnBotCreated(() => r.SetSpeakerAndResponder(findOrCreateUser, BotAsUser));
            }
            if (isToplevel) findOrCreateUser.CurrentRequest = r;
            r.depth = 0;
            r.IsTraced = findOrCreateUser.IsTraced;
            return r;
        }
       
        /// <summary>
        /// Given a request containing user input, produces a result from the Proccessor
        /// </summary>
        /// <param name="request">the request from the user</param>
        /// <returns>the result to be output to the user</returns>
        ///
        public Result ChatWR(Request request)
        {
            Result requestCurrentResult = request.FindOrCreateCurrentResult();
            Result result = ChatWithRequest(request, requestCurrentResult, request.IsToplevelRequest,
                                            request.RequestType, request.SaveResultsOnJustHeard);
            if (!result.Started)
            {
                writeDebugLine("result unstarted " + result);
                return result;
            }
            return result;
        }

        public Result ChatWithRequest(Request request, Result parentResultIn, bool isToplevel, RequestKind requestType, bool saveResultsOnConverstion)
        {
            if (!TryRestorableUserRequest && ((useServitor) && (servitor != null)))
            {
                Logger.Warn("Servitor code should not be calling this!");
                User curUser = parentResultIn.Requester;
                string input = request.ChatInput.OrignalRawText.ToString();
                string answer = servitor.respondToChat(input, curUser, isToplevel, requestType);
                Result result = request.CreateResult(request);
                result.SetOutput = answer;
                return result;
            }
            if (!TryRestorableUserRequest && ((useServitor) && (servitor == null)))
            {
                Result result = request.CreateResult(request);
                result.SetOutput = "Servitor nulled at this time.";
                return result;
            }
            User target = request.Responder;
            User user = request.Requester;
            SettingsDictionary userPredicates = user.Predicates;
            bool isTraced = request.IsTraced | request.IsToplevelRequest;

            UndoStack undoStackSession = UndoStack.GetStackFor(new object());
            bool queryFailed = true;
            Unifiable requestrawInput = request.rawInput.Trim();

            undoStackSession.pushValues(userPredicates, "i", user.UserName);
            if (saveResultsOnConverstion)
            {
                undoStackSession.pushValues(userPredicates, "rawinput", requestrawInput);
                undoStackSession.pushValues(userPredicates, "input", requestrawInput);
            }
           
            if (target != null && target.UserName != null)
            {
                undoStackSession.pushValues(userPredicates, "you", target.UserName);
                SettingsDictionary targetPredicates = target.Predicates;
                undoStackSession.pushValues(targetPredicates, "you", user.UserName);
                undoStackSession.pushValues(targetPredicates, "i", target.UserName);

            }
            //lock (user.QueryLock)
            {
                ChatLabel label = request.PushScope;
                Result allResults = null;
                try
                {
                    if (TryRestorableUserRequest)
                    {
                        allResults = ChatSensitiveToQState(request, request.graphName, request.IsToplevelRequest);
                    } else
                    {
                        allResults = ChatWithToplevelResults(request, parentResultIn, isToplevel, requestType);
                    }
                    /*
                    // ReSharper disable ConditionIsAlwaysTrueOrFalse
                    if (res.OutputSentenceCount == 0 && false)
                    // ReSharper restore ConditionIsAlwaysTrueOrFalse
                    {
                        request.UndoAll();
                        request.IncreaseLimits(1);
                         res = ChatWithRequest4(request, user, target, G);
                    }
                     */
                    servitor.MaybeUpdateBotJustSaidLastOutput(isToplevel, requestType, user,allResults.Output, false,false,false);
                    if (request.IsToplevelRequest)
                    {
                        AddSideEffectHook(request, user, parentResultIn);
                        if (request.SaveResultsOnJustHeard && saveResultsOnConverstion)
                        {
                            user.JustSaid = requestrawInput;
                            if (target != null)
                                target.JustSaid = user.That; //.Output;
                        }
                    }
                    queryFailed = false;
                    return allResults;
                }
                catch (ChatSignalOverBudget e)
                {
                    request.UndoAll();
                    writeToLog("ChatWithUser ChatSignalOverBudget: ( request.UndoAll() )" + request + " " + e);
                    return (Result) e.result;
                }
                catch (ChatSignal e)
                {
                    if (label.IsSignal(e)) return (Result) request.CurrentResult;
                    throw;
                }
                finally
                {
                    if (saveResultsOnConverstion) AddHeardPreds(parentResultIn.RawOutput, HeardPredicates);
                    request.CommitSideEffects(false);
                    label.PopScope();
                    undoStackSession.UndoAll();
                    if (queryFailed)
                    {
                        request.UndoAll();
                    } else
                    {
                        request.CommitSideEffects(true);
                    }
                    request.Exit();
                    request.SetSpeakerAndResponder(user, parentResultIn.Responder.Value);
                }
            }
        }

        private void AddSideEffectHook(Request request, User originalRequestor, Result res)
        {
            request.AddSideEffect("Populate User the Result object",
                                  () =>
                                  {
                                      PopulateUserWithResult(originalRequestor, request, res);
                                  });
        }

        public Result ChatWithToplevelResults(Request request, Result parentResult, bool isToplevel, RequestKind requestType)
        {
            Logger.Warn("In TODO code");
            var originalRequestor = request.Requester;
            var originalTargetUser = request.Responder;
            ChatLabel label = request.PushScope;
            try
            {
                GraphMaster G = request.Graph;
                if (!request.IsAllowedGraph(G))
                {
                    writeToLog("ChatWithRequest4: DisallowedGraphs " + G);
                    return (Result) request.CurrentResult;
                }
                string why = request.WhyComplete;
                if (why != null)
                {
                    writeToLog("ChatWithRequest4: WhyComplete " + why);
                    return (Result) request.CurrentResult;
                }

                Result result = ChatFor1Result(request, parentResult, requestType);
                if (result.OutputSentences.Count != 0)
                {
                    result.RotateUsedTemplates();
                }
                return (Result)result;
            }
            catch (ChatSignalOverBudget e)
            {
                writeToLog("ChatWithRequest4 ChatSignalOverBudget: " + request + " " + e);
                return (Result)request.CurrentResult;
            }
            catch (ChatSignal e)
            {
                if (label.IsSignal(e)) return (Result)request.CurrentResult;
                throw;
            }
            finally
            {
                label.PopScope();
                request.SetSpeakerAndResponder(originalRequestor, originalTargetUser);
            }
        }

        public Result ChatFor1Result(Request request, Result parentResult, RequestKind kind)
        {
            Logger.Warn("In TODO code");
            //result = request.CreateResult(request);
            User originalRequestor = request.Requester;
            //LastUser = user; 
            Result childResult;
            GraphMaster G = request.Graph;
            bool isTraced = request.IsTraced || G == null;
            OutputDelegate writeToLog = this.writeToLog;
            string rr = request.rawInput;
            if (rr.StartsWith("@") || (rr.IndexOf("<") + 1 < rr.IndexOf(">")))
            {
                childResult = ChatWithNonGraphmaster(request, parentResult, G, isTraced, writeToLog, kind);
            }
            else if (request.GraphsAcceptingUserInput)
            {
                childResult = ChatUsingGraphMaster(request, parentResult, G, isTraced, writeToLog, kind);
            }
            else
            {
                childResult = request.CreateResult(request);
                string nai = NotAcceptingUserInputMessage;
                if (isTraced) this.writeToLog("ERROR {0} getting back {1}", request, nai);
                request.AddOutputSentences(null, nai, parentResult, 1.0);
            }
            User popu = (originalRequestor ?? request.Requester ?? parentResult.Requester).Value;
            parentResult.IsComplete = true;
            childResult = childResult ?? parentResult;
            parentResult.SetOutput = childResult.RawOutput;
            popu.addRequestTemplates(request);
            if (streamDepth > 0) streamDepth--;
            return (Result) childResult;
        }

        private Result ChatWithNonGraphmaster(Request request, Result result, GraphMaster G, bool isTraced, OutputDelegate writeToLog, RequestKind requestType)
        {
            Logger.Warn("In TODO code");
            writeToLog = writeToLog ?? DEVNULL;
            isTraced = request.IsTraced;
            //chatTrace = null;

            streamDepth++;

            string rawInputString = request.ChatInput.OrignalRawText;// rawInput.AsString();

            if (rawInputString.StartsWith("@"))
            {
                result = request.CreateResult(request);
                if (chatTrace) result.IsTraced = isTraced;
                StringWriter sw = new StringWriter();

                bool myBotBotDirective = BotDirective(request.Requester, request, rawInputString, sw.WriteLine);
                string swToString = sw.ToString();
                if (writeToLog != null) writeToLog("ChatWithNonGraphmaster: " + swToString);
                else
                {
                    writeToLog = StaticAIMLUtils.DEVNULL;
                }
                if (myBotBotDirective)
                {
                    Result requestCurrentResult = result ?? request.CurrentResult;
                    if (requestCurrentResult != null)
                    {
                        requestCurrentResult.SetOutput = swToString;
                    }
                    return result;
                }
                writeToLog("ERROR: cannot find command " + request.rawInput);
                return null;
            }
            ChatLabel label = request.PushScope;
            var orig = request.ResponderOutputs;
            if (AlwaysUseImmediateAimlInImput && StaticAIMLUtils.ContainsAiml(rawInputString))
            {
                try
                {
                    var tn = StaticAIMLUtils.getTemplateNode(rawInputString);
                    //tn = getDocNode( rawInputString , false, false, StringOnlyDoc);
                    result = ImmediateAiml(tn, request, Loader, requestType);
                    //request.rawInput = result.Output;
                    return result;
                }
                catch (ChatSignal e)
                {
                    if (label.IsSignal(e)) return e.result;
                    throw;
                }
                catch (Exception e)
                {
                    isTraced = true;
                    this.writeToLog(e);
                    writeToLog("ImmediateAiml: ERROR: " + e);
                    label.PopScope();
                    return null;
                }
            }
            return null;
        }
      
        private Result ChatUsingGraphMaster(Request request, Result result, GraphMaster G, bool isTraced, OutputDelegate writeToLog, RequestKind kind)
        {
            Logger.Warn("In TODO code");
            //writeToLog = writeToLog ?? DEVNULL;
            {
                Utterance utterance = request.ChatInput;// Utterance.GetParsedSentences(request, isTraced, writeToLog);

                bool printedSQs = false;
                G = G ?? request.Graph ?? DefaultStartGraph;

                var srequest = request.GetQuerySettings().GetSettable();
                // grab the templates for the various sentences from the graphmaster
                srequest.IsTraced = isTraced;
                //result = request.CreateResult(request);

                // load the queries
                List<GraphQuery> AllQueries = new List<GraphQuery>();

                bool topleveRequest = request.IsToplevelRequest;

                int UNLIMITED = 1000000;
                srequest.MaxOutputs = UNLIMITED;
                srequest.MaxPatterns = UNLIMITED;
                srequest.MaxTemplates = UNLIMITED;

                // Gathers the Pattern SubQueries!
                int sentNum = -1;
                var user = request.Requester;
                foreach (Unifiable sentence in utterance.EnglishSentences)
                {
                    sentNum++;
                    if (request.IsToplevelRequest)
                    {
                        string english = utterance.EnglishSentences[sentNum];
                        if (NatLangDb.WasQuestion(english))
                        {
                            request.Requester.Predicates.updateSetting("question", english);
                        }
                    }
                    List<string> paths = gatherPaths(G.ScriptingName, user, sentence,
                                                     user.getPreStates(), user.getPostStates(),
                                                     request.Loader);
                    SortPaths(paths, G.getPathScore);
                    foreach (var path in paths)
                    {
                        AllQueries.Add(G.gatherQueriesFromGraph(path, request, MatchState.State));
                    }
                }
                try
                {
                    // gather the templates and patterns
                    foreach (var ql in AllQueries)
                    {
                        if (topleveRequest)
                        {
                            ql.TheRequest.SuspendSearchLimits = true;
                            ql.NoMoreResults = false;
                            ql.MaxTemplates = UNLIMITED;
                            ql.MaxPatterns = UNLIMITED;
                            ql.MaxOutputs = UNLIMITED;
                        }
                        G.RunGraphQuery(ql, kind);
                        //if (request.IsComplete(result)) return result;
                    }
                    foreach (var ql in AllQueries)
                    {
                        request.TopLevelQuery = ql;
                        if (chatTrace) result.IsTraced = isTraced;
                        if (ql.PatternCount > 0)
                        {
                            request.TopLevelQuery = ql;
                            // if (ql.TemplateCount > 0)
                            {
                                request.TopLevelQuery = ql;
                                result.AddSubqueries(ql);
                            }
                            var kept = ql.PreprocessSubQueries(request, result.SubQueries, isTraced, ref printedSQs,
                                                               writeToLog);
                        }
                        // give a 20 second blessing
                        if (false && result.SubQueries.Count > 0 && !request.SraiDepth.IsOverMax)
                        {
                            writeToLog("Extending time");
                            request.TimeOutFromNow = TimeSpan.FromSeconds(2);
                        }
                        //ProcessSubQueriesAndIncreasLimits(request, result, ref isTraced, printedSQs, writeToLog);
                    }
                    int solutions;
                    bool hasMoreSolutions;
                    CheckResult(request, result, out solutions, out hasMoreSolutions);
                }
                catch (ChatSignal exception)
                {
                    writeToLog("ChatSignalOverBudget: " + exception.Message);
                }
            }
            return result;
        }

        private void ProcessSubQueriesAndIncreasLimits(Request request, Result result, ref bool isTraced, bool printedSQs, OutputDelegate writeToLog)
        {
            writeToLog = writeToLog ?? StaticAIMLUtils.DEVNULL;
            int sqc = result.SubQueries.Count;
            int solutions;
            bool hasMoreSolutions;
            CheckResult(request, result, out solutions, out hasMoreSolutions);
            return;
            if (result.OutputSentenceCount == 0 || sqc == 0)
            {
                return;
                //  string oldSets = QuerySettings.ToSettingsString(request.GetQuerySettings());
                isTraced = true;
                //todo pick and chose the queries
                int n = 0;
                while (result.OutputSentenceCount == 0 && n < 3 && request.depth < 3)
                {
                    n++;
                    request.UndoAll();
                    request.CurrentSettings.IncreaseLimits(1);
                    CheckResult(request, result, out solutions, out hasMoreSolutions);
                    if (result.OutputSentenceCount != 0 || sqc != 0)
                    {
                    }
                    // string newSets = QuerySettings.ToSettingsString(request.GetQuerySettings());
                    //writeToLog("AIMLTRACE: bumped up limits " + n + " times for " + request + "\n --from\n" + oldSets + "\n --to \n" +
                    //         newSets);
                }
            }
            // process the templates into appropriate output
            PostProcessSubqueries(request, result, isTraced, writeToLog);
        }
        private void PostProcessSubqueries(Request request, Result result, bool isTraced, OutputDelegate writeToLog)
        {
            Logger.Warn("In TODO code");
            writeToLog = writeToLog ?? StaticAIMLUtils.DEVNULL;
            {
                if (isTraced)
                {
                    if (result.OutputSentenceCount != 1 && !request.Graph.UnTraced)
                    {
                        DLRConsole.SystemFlush();
                        string s = "AIMLTRACE: result.OutputSentenceCount = " + result.OutputSentenceCount;
                        foreach (string path in result.OutputSentences)
                        {
                            s += Environment.NewLine;
                            s += "  " + Unifiable.ToVMString(path);
                        }
                        s += Environment.NewLine;
                        writeToLog(s);
                        DLRConsole.SystemFlush();
                    }

                    foreach (SubQuery path in result.SubQueries)
                    {
                        //string s = "AIMLTRACE QUERY:  " + path.FullPath;

                        //writeToLog("\r\n tt: " + path.Request.Graph);
                        if (chatTrace)
                        {
                            //bot.writeChatTrace("\"L{0}\" -> \"{1}\" ;\n", result.SubQueries.Count, path.FullPath.ToString());
                            writeChatTrace("\"L{0}\" -> \"L{1}\" ;\n", result.SubQueries.Count - 1,
                                           result.SubQueries.Count);
                            writeChatTrace("\"L{0}\" -> \"REQ:{1}\" ;\n", result.SubQueries.Count, request.ToString());
                            writeChatTrace("\"REQ:{0}\" -> \"PATH:{1}\" [label=\"{2}\"];\n", request.ToString(),
                                           result.SubQueries.Count, path.FullPath);
                            writeChatTrace("\"REQ:{0}\" -> \"RPY:{1}\" ;\n", request.ToString(), result.RawOutput);
                        }
                    }
                }
            }
        }
        internal void PopulateUserWithResult(User user0, Request request, Result result)
        {
            Logger.Warn("In TODO code");
            User user = (user0 ?? request.Requester ?? result.Requester).Value;
            // toplevel result
            var info = result.ProofTemplate();
            // only the toplevle query popuklates the user object
            if (result.ParentResult == null)
            {
                if (info != null)
                {
                    lock (user.TemplatesLock) user.ProofTemplates.Add(info);
                }               
                user.addResult(result);
                if (RotateUsedTemplate)
                {
                    result.RotateUsedTemplates();
                }
            }
            else
            {
                if (info != null)
                {
                    user.UsedChildTemplates.Add(info);
                }           
            }
        }

        public Unifiable EnsureEnglish(Unifiable arg)
        {
            // ReSharper disable ConvertToConstant.Local
            bool DoOutputSubst = false;
            // ReSharper restore ConvertToConstant.Local
            // ReSharper disable ConditionIsAlwaysTrueOrFalse
            if (DoOutputSubst)
            // ReSharper restore ConditionIsAlwaysTrueOrFalse
            {
                string sentence = ApplySubstitutions.Substitute(OutputSubstitutions, arg);
                sentence = TextPatternUtils.ReTrimAndspace(sentence);
                if (TextPatternUtils.DifferentBesidesCase(arg, sentence))
                {
                    writeDebugLine("OutputSubst: " + arg + " -> " + sentence);
                    arg = sentence;
                }
            }
            return arg;

            return ToEnglish(arg);
        }

        public delegate void InputParser(Request request, IEnumerable<Unifiable> rawSentences);
        /*
        private string swapPerson(string inputString)
        {
            if (Loader == null)
            {
                Loader = new AIMLLoader(this, GetBotRequest("swapPerson " + inputString));
            }
            string temp = Loader.NormalizeU(inputString, true);
            //temp = ApplySubstitutions.Substitute(this, this.PersonSubstitutions, temp);
            temp = ApplySubstitutions.Substitute(Person2Substitutions, temp);
            return temp;
        }
        */
        static public Unifiable CleanupCyc(string text)
        {
            if (text == null) return null;
            if (text == "")
            {
                return "";
            }
            text = Trim(text);
            if (text == "")
            {
                writeDebugLine(" had white string ");
                return "";
            }
            if (TheCycS != null)
            {
                text = TheCycS.CycCleanupCyc(text);
            }
            var thinkTHINKYTAG = think.THINKYTAG;
            if (string.IsNullOrEmpty(thinkTHINKYTAG)) thinkTHINKYTAG = "TAG-THINK";
            return Trim(CleanNops(text.Replace("#$", " ").Replace(thinkTHINKYTAG, " ").Replace("  ", " ")));
        }


        private void CheckResult(Request request, Result result, out int solutions, out bool hasMoreSolutions)
        {
            var isTraced = request.IsTraced || result.IsTraced;
            hasMoreSolutions = false;
            solutions = 0;
            // checks that we have nothing to do
            if (result == null || result.SubQueries == null)
            {
                if (isTraced)
                {
                    writeToLog("NO QUERIES FOR " + request);
                }
                return;
            }

            List<SubQuery> AllQueries = GetQueriesFromResults(request, result, isTraced);
            if (AllQueries == null || AllQueries.Count == 0)
            {
                if (isTraced)
                {
                 //   writeToLog("NO QUERIES FOR RESULTS " + request);
                }
                return;
            }
            List<SubQuery> sortMe = SortCandidateSolutions(AllQueries, isTraced);
            if (sortMe == null || sortMe.Count == 0)
            {
                //if (isTraced)
                {
                    writeToLog("NO SORTED QUERIES " + request);
                }
                return;
            }
            solutions = GetSolutions(request, result, sortMe, solutions, out hasMoreSolutions);
        }

        private List<SubQuery> GetQueriesFromResults(Request request, Result result, bool isTraced)
        {
            List<SubQuery> AllQueries = new List<SubQuery>();
            //int solutions;
            List<SubQuery> resultSubQueries = result.SubQueries;

            List<TemplateInfo> AllTemplates = new UList();
            bool found1 = false;
            lock (resultSubQueries)
            {
                foreach (SubQuery query in resultSubQueries)
                {
                    var queryTemplates = query.Templates;
                    if (queryTemplates == null || queryTemplates.Count == 0) continue;
                    AllQueries.Add(query);
                    if (!found1) found1 = true;
                    lock (queryTemplates)
                        AllTemplates.AddRange(query.Templates);
                }
            }

            if (!found1)
            {
                //solutions = 0;
                //hasMoreSolutions = false;
                result.TemplatesSucceeded = 0;
                result.OutputsCreated = 0;
                //return;
            }
            return AllQueries;
        }

        private List<SubQuery> SortCandidateSolutions( List<SubQuery> AllQueries, bool isTraced)
        {
            Logger.Warn("In TODO code");
            if (!BE_COMPLETE_NOT_FAST)
            {
                if (isTraced)
                {
                    PrintQueryList("-- ", AllQueries);
                } 
                return AllQueries;
            }
            List<SubQuery> sortMe = new List<SubQuery>(AllQueries);
            
            sortMe.Sort();

            bool countChanged = sortMe.Count != AllQueries.Count;
            for (int index = 0; index < sortMe.Count; index++)
            {
                var subQuery = sortMe[index];
                var allQ = AllQueries[index];
                if (subQuery.EqualsMeaning(allQ))
                {
                    continue;
                }
                countChanged = true;
            }
            if (isTraced || countChanged)
            {
                string cc = countChanged ? "QUERY SAME " : "QUERY SORT ";
                writeToLog("--------------------------------------------");
                if (countChanged)
                {

                    int sqNum = 0;
                    writeToLog("AllQueries.Count = " + sortMe.Count + " was " + AllQueries);
                    if (false) PrintQueryList("---BEFORE QUERY ", AllQueries);
                }
                if (isTraced)
                {
                    PrintQueryList("--- " + cc + " ", AllQueries);
                }
                writeToLog("--------------------------------------------");
            }
            return sortMe;
        }

        private void PrintQueryList(string cc, List<SubQuery> AllQueries)
        {
            int sqNum = 0;
            foreach (SubQuery query in AllQueries)
            {
                writeToLog(cc + sqNum + ": " + query.Pattern + " " + query.Pattern.Graph);
                sqNum++;
            }
        }

        private int GetSolutions(Request request, Result result, List<SubQuery> sortMe, int solutions, out bool hasMoreSolutions)
        {
            Logger.Warn("In TODO code");
            request.Requester.addSetting("inputreq", request.rawInput);
            foreach (SubQuery query in sortMe)
            {
                if (result.IsComplete)
                {
                    hasMoreSolutions = false;
                    return solutions;
                }
                result.CurrentQuery = query;
                foreach (TemplateInfo s in query.Templates)
                {
                    hasMoreSolutions = true;
                    try
                    {
                        s.Query = query;
                        // Start each the same
                        var lastHandler = ProcessQueryTemplate(request, s.Query, s, result, request.LastHandlerU,
                                                               ref solutions,
                                                               out hasMoreSolutions);
                        if (result.IsComplete)
                        {
                            hasMoreSolutions = false;
                            return solutions;
                        }
                    }
                    catch (ChatSignal)
                    {
                        if (!request.IsToplevelRequest) throw;
                    }
                }
            }
            hasMoreSolutions = false;
            result.CurrentQuery = null;
            return solutions;
        }

        private AIMLTagHandler ProcessQueryTemplate(Request request, SubQuery query, TemplateInfo s, Result result, AIMLTagHandler lastHandlerU, ref int solutions, out bool hasMoreSolutions)
        {
            AIMLTagHandler childHandlerU = null;
            request.TopLevelScore = 1.0;
            hasMoreSolutions = false;
            try
            {
                request.CurrentQuery = query;
                s.Query = query;
                query.CurrentTemplate = s;
                bool createdOutput;
                bool templateSucceeded;
                XmlNode sOutput = s.ClonedOutput;
                childHandlerU = TagHandling.proccessResponse(query, request, result, sOutput, s.Guard, out createdOutput,
                                               out templateSucceeded, lastHandlerU, s, false, false);
                solutions++;
                query.CurrentTagHandlerU = childHandlerU;
                request.LastHandlerU = lastHandlerU;
                if (templateSucceeded)
                {
                    result.TemplatesSucceeded++;
                    s.OnTemplatesSucceeded(query, request);
                }
                else
                {
                    s.OnTemplatesFailed(query, request);
                }
                if (createdOutput)
                {
                    result.OutputsCreated++;
                    s.OnOutputsCreated(query, request);
                    hasMoreSolutions = true;
                    //break; // KHC: single vs. Multiple
                    if (((QuerySettingsReadOnly)request).ProcessMultipleTemplates == false)
                    {
                        if (request.IsComplete(result))
                        {
                            hasMoreSolutions = false;
                            return lastHandlerU;
                        }
                    }
                }
                return childHandlerU;
            }
            catch (ChatSignal e)
            {
                throw;
            }
            catch (Exception e)
            {
                writeToLog(e);
                if (WillCallHome)
                {
                    phoneHome(e.Message, request);
                }
                writeToLog("WARNING! A problem was encountered when trying to process the input: " +
                           request.rawInput + " with the template: \"" + s + "\"");
                hasMoreSolutions = false;
            }
            return childHandlerU;
        }

        public void writeChatTrace(string message, params object[] args)
        {
            if (!chatTrace) return;
            try
            {
                lock (chatTraceO)
                {
                    if (chatTraceW == null)
                    {
                        chatTraceW = new StreamWriter("bgm\\chatTrace.dot");
                        chatTraceW.WriteLine("digraph G {");
                        streamDepth = 1;
                    }
                    if (streamDepth < 0) streamDepth = 0;
                    int w = streamDepth;
                    while (w-- < 0)
                    {
                        chatTraceW.Write("  ");
                    }
                    message = SafeFormat(message, args);
                    chatTraceW.WriteLine(message);
                    //writeDebugLine(message);
                    if (streamDepth <= 0 && chatTraceW != null)
                    {
                        chatTraceW.WriteLine("}");
                        chatTraceW.Close();
                        streamDepth = 0;
                        chatTraceW = null;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public Result ImmediateAiml(XmlNode templateNode, Request request0,
                                            AIMLLoader loader, RequestKind requestKind)
        {
            Result masterResult = request0.CreateResult(request0);
            bool prev = request0.GraphsAcceptingUserInput;
            try
            {
                request0.GraphsAcceptingUserInput = true;
                var mr = ImmediateAIMLNode(request0, templateNode, requestKind);
                return mr;
            }
            catch (ChatSignal ex)
            {
                writeToLog(ex);
                return masterResult;
            }
            catch (Exception ex)
            {
                writeToLog(ex);
                return masterResult;
            }
            finally
            {
                request0.GraphsAcceptingUserInput = prev;
            }
        }

        private Result ImmediateAIMLNode(Request parentRequest, XmlNode templateNode, RequestKind requestKind)
        {
            string requestName = StaticAIMLUtils.ToTemplateXML(templateNode);
            AltBot request0Proccessor = this;
            Unifiable sGuard = null;
            Request request = null;
            User user = BotAsUser;

            if (parentRequest != null)
            {
                user = parentRequest.Requester;
                requestName = parentRequest.rawInput + " " + requestName;
            }

            //  if (request == null)

            request = new Request(requestName, user, default(LoaderOptions), parentRequest.Responder, request0Proccessor,
                                  parentRequest, parentRequest.Graph, false, requestKind);

            if (parentRequest != null)
            {
                if (parentRequest != request) request.Graph = parentRequest.Graph;
                //request.depth = parentRequest.depth + 1;
            }

            Result result = request.CreateResult(request);
            //request.CurrentResult = result;
            if (request.CurrentResult != result)
            {
                writeToLog("ERROR did not set the result!");
            }
            if (request.Graph != result.Graph)
            {
                writeToLog("ERROR did not set the Graph!");
            }
            TemplateInfo templateInfo = null; //
            if (false)
            {
              //  templateInfo = new TemplateInfo(templateNode, null, null, null);
            }
            bool templateSucceeded;
            bool createdOutput;
            SubQuery query = request.CurrentQuery;
            bool doUndos = false;
            bool copyChild = templateNode.IsReadOnly;
            if (query == null)
            {
                query = new SubQuery(requestName, result, request);
                request.IsTraced = true;
                doUndos = true;
                copyChild = true;
            }
            if (copyChild)
            {
                if (templateInfo != null)
                    templateNode = templateInfo.ClonedOutput;
                copyChild = false;
            }
            var lastHandler = TagHandling.proccessResponse(query, request, result, templateNode, sGuard, out createdOutput, out templateSucceeded,
                             (AIMLTagHandler)null, templateInfo, copyChild, false); //not sure if should copy parent
            if (doUndos) query.UndoAll();
            request.LastHandlerU = lastHandler;
            return result;
        }

        internal bool IsOutputSentence(string sentence, string outputSentence)
        {
            if (outputSentence != null)
            {
                if ((" " + outputSentence+" ").ToUpper().Contains(" BR "))
                {
                    return true;
                }
            }
            if (sentence == null) return false;
            string o = ToEnglish(sentence);
            if (o == null) return false;
            return o.Length > 0;
        }


        public string ToHeard(string message)
        {
            string trim = StaticAIMLUtils.ToEmptyOrNot(message);
            if (string.IsNullOrEmpty(trim)) return trim;

            if (message.StartsWith("  "))
            {
                return message;
            }
            message = CleanNops(trim);
            message = Trim(message);
            if (message == "") return "";
            if (false && message.Contains("<"))
            {
                string messageIn = message;
                message = StaticAIMLUtils.ForInputTemplate(message);
                //if (messageIn != message) writeDebugLine("heardSelfSay - ForInputTemplate: " + messageIn + " -> " + message);
            }

            if (message == "") return "";
            //if (message.Contains("<"))
            {
                string messageIn = message;
                message = ToEnglish(message);
                if (messageIn != message) writeDebugLine("heardSelfSay - ToEnglish: " + messageIn + " -> " + message);
            }

            if (message == "" || message.Contains("<"))
            {
                writeDebugLine("heardSelfSay - ERROR: heard ='" + message + "'");
                return message;
            }
            return "  " + message;
        }

        public static string CleanNops(string message)
        {
            string testBR = (" " + message + " ");
            if (testBR.ToUpper().Contains(" BR "))
            {
                message =
                    ReTrimAndspace(StaticAIMLUtils.ReplacePairs(message, " br ", " \n ", " BR ", " \n ", " NOP ", " ", " nop ", " "));
            }
            return message;
        }

       
        public string ToEnglish(string sentenceIn)
        {
            if (sentenceIn == null)
            {
                return null;
            }
            sentenceIn = CleanNops(sentenceIn);
 
            sentenceIn = ReTrimAndspace(sentenceIn);
            if (sentenceIn == "")
            {
                return "";
            }

            string sentence = StaticAIMLUtils.VisibleRendering(StaticAIMLUtils.getTemplateNode(sentenceIn).ChildNodes,
                                               StaticAIMLUtils.PatternSideRendering);

            sentence = ReTrimAndspace(sentence);
            if (DifferentBesidesCase(sentenceIn, sentence))
            {
                writeToLog("VisibleRendering: " + sentenceIn + " -> " + sentence);
                sentenceIn = sentence;
            }

            if (sentence == "")
            {
                return "";
            }

            const bool DoInputSubsts = false;
            // ReSharper disable ConditionIsAlwaysTrueOrFalse
            if (DoInputSubsts)
            // ReSharper restore ConditionIsAlwaysTrueOrFalse
            {
                sentenceIn = ToInputSubsts(sentenceIn);
            }

            sentence = CleanupCyc(sentenceIn);
            sentence = ReTrimAndspace(sentence);
            if (DifferentBesidesCase(sentenceIn, sentence))
            {
                writeToLog("CleanupCyc: " + sentenceIn + " -> " + sentence);
                if (sentence != "") sentenceIn = sentence;
                if (sentenceIn == "")
                {
                    return "";
                }
            }

            sentence = ApplySubstitutions.Substitute(OutputSubstitutions, sentenceIn);
            sentence = ReTrimAndspace(sentence);
            if (DifferentBesidesCase(sentenceIn, sentence))
            {
                writeToLog("OutputSubst: " + sentenceIn + " -> " + sentence);
                sentenceIn = sentence;
            }

            if (!StaticAIMLUtils.checkEndsAsSentence(sentenceIn))
            {
                return sentenceIn;// += ".";
            }

            return sentenceIn;
        }

        public string ToInputSubsts(string sentenceIn)
        {
            string sentence;
            sentence = ApplySubstitutions.Substitute(InputSubstitutions, sentenceIn);
            //sentence = string.Join(" ", sentence.Split(toCharArray, StringSplitOptions.RemoveEmptyEntries));
            sentence = StaticAIMLUtils.ReTrimAndspace(sentence);
            if (DifferentBesidesCase(sentenceIn, sentence))
            {
                writeToLog("InputSubst: " + sentenceIn + " -> " + sentence);
                sentenceIn = sentence;
            }
            return sentenceIn;
        }

        #endregion

        private void SetupConveration()
        {
            //HeardSelfSayQueue.Start();
            AddBotCommand("do16", () =>
                                      {
                                          Enqueue(() => Sleep16Seconds(10));
                                          Enqueue(() => Sleep16Seconds(1));
                                          Enqueue(() => Sleep16Seconds(30));
                                          Enqueue(() => Sleep16Seconds(5));
                                          Enqueue(() => Sleep16Seconds(5));
                                          Enqueue(() => Sleep16Seconds(45));
                                      });

            string names_str = "markovx.trn 5ngram.ngm";
            var nameset = names_str.Split(' ');
            foreach (string name in nameset)
            {
                int loadcount = 0;
                string file = HostSystem.Combine("trn", name);
                if (HostSystem.FileExists(file))
                {
                    StreamReader sr = new StreamReader(file);
                    writeToLog(" **** Markovian Brain LoadMarkovLTM: '{0}'****", file);
                    MBrain.Learn(sr);
                    sr.Close();
                    writeToLog(" **** Markovian Brain initialized.: '{0}' **** ", file);
                    loadcount++;
                }

                file = HostSystem.Combine("ngm", name);
                if (HostSystem.FileExists(file))
                {
                    StreamReader sr = new StreamReader(file);
                    writeToLog(" **** Markovian Brain LoadMarkovLTM: '{0}'**** ", file);
                    MBrain.LearnNgram(sr);
                    sr.Close();
                    writeToLog(" **** Markovian Brain N-Gram initialized '{0}'. **** ", file);
                    loadcount++;
                }

                if (loadcount == 0)
                {
                    writeToLog(
                        " **** VVARNING: No Markovian Brain Training nor N-Gram file found for '{0}' . **** ", name);
                }
            }

            if (pHMM.hmmCorpusLoaded == 0)
            {
                string file = HostSystem.Combine("bgm", "corpus.txt");
                //if (HostSystem.DirExists(file))
                if (HostSystem.FileExists(file))
                {
                    writeToLog("Load Corpus Bigrams: '{0}'", file);
                    StreamReader sr = new StreamReader(file);
                    pHMM.LearnBigramFile(sr);
                    sr.Close();
                    pHMM.hmmCorpusLoaded++;
                    writeToLog("Loaded Corpus Bigrams: '{0}'", file);
                }
            }
            initWordNet(PathToWordNet);
            if (this.LuceneIndexer == null)
            {
                Console.WriteLine("*** Start Lucene ***");
                var myLuceneIndexer = new MyLuceneIndexer(PathToLucene, fieldName, this, wordNetEngine);
                this.LuceneIndexer = this.LuceneIndexer;
                myLuceneIndexer.TheBot = this;
                TripleStore = myLuceneIndexer.TripleStoreProxy;
                Console.WriteLine("*** DONE Lucene ***");
            }
            else
            {
                Console.WriteLine("*** REUSING Lucene ***");
            }
        }

        public void initWordNet(string wordNetPath)
        {
            lock (AltBot.WordNetEngineLock)
            {
                if (wordNetEngine == null)
                {
                    Console.WriteLine("*** Start WN-Load ***");
                    wordNetEngine = new WordNetEngine(PathToWordNet, true);
                    Console.WriteLine("*** DONE WN-Load ***");
                }
                else
                {
                    Console.WriteLine("*** REUSING WN-Load ***");
                }
                var myLuceneIndexer = this.LuceneIndexer as MyLuceneIndexer;
                if (myLuceneIndexer != null)
                {
                    myLuceneIndexer.wordNetEngine = wordNetEngine;
                }
            }
        }

        private int napNum = 0;

        public static DateTime Now
        {
            get { return DateTime.Now; }
        }

        private void Sleep16Seconds(int secs)
        {
            napNum++;
            DateTime start = AltBot.Now;
            var errOutput = DLRConsole.SYSTEM_ERR_WRITELINE;
            string thisTime = " #" + napNum;
            try
            {
                errOutput("START Sleep" + secs + "Seconds " + thisTime + " \ntime=" + AltBot.Now.Subtract(start).TotalMilliseconds);
                Thread.Sleep(TimeSpan.FromSeconds(secs));
                errOutput("COMPLETE Sleep" + secs + "Seconds " + thisTime + " \ntime=" + AltBot.Now.Subtract(start).TotalSeconds);
                Enqueue("ENQUE Sleep" + secs + "Seconds #" + thisTime, () => Sleep16Seconds(secs));

            }
            catch (ThreadAbortException e)
            {
                errOutput("ThreadAbortException Sleep" + secs + "Seconds " + e + " " + thisTime + " \ntime=" + AltBot.Now.Subtract(start).TotalSeconds);
            }
            catch (System.Threading.ThreadInterruptedException e)
            {
                errOutput("ThreadInterruptedException Sleep" + secs + "Seconds " + e + " " + thisTime + " \ntime=" + AltBot.Now.Subtract(start).TotalSeconds);
            }
            catch (Exception e)
            {
                errOutput("Exception Sleep" + secs + "Seconds " + e + " " + thisTime + " \ntime=" + AltBot.Now.Subtract(start).TotalSeconds);
                throw;
            }
            finally
            {
                errOutput("Finanaly Sleep" + secs + "Seconds #" + thisTime + " \ntime=" + AltBot.Now.Subtract(start).TotalSeconds);
            }
        }


        internal void Enqueue(ThreadStart action)
        {
            HeardSelfSayQueue.Enqueue(HeardSelfSayQueue.NamedTask("Enqueue_" + napNum, action));
        }
        internal void Enqueue(string named, ThreadStart action)
        {
            HeardSelfSayQueue.Enqueue(HeardSelfSayQueue.NamedTask(named, action));
        }

        private object ChatWithThisBot(string cmd, Request request)
        {
            Request req = request.CreateSubRequest(cmd, request.Graph,
                                                   RequestKind.ChatForString | RequestKind.BotPropertyEval);
            req.SetSpeakerAndResponder(req.Requester, BotAsUser);
            req.IsToplevelRequest = request.IsToplevelRequest;
            return LightWeigthBotDirective("@locally " + cmd, req);
        }
    }
}