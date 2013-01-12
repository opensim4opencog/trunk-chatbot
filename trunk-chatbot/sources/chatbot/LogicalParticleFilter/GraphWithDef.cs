#define MERGED_RDFSTORE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web;
using LogicalParticleFilter1;
using MushDLR223.Utilities;
using VDS.RDF;
using VDS.RDF.Nodes;
using VDS.RDF.Parsing;
using VDS.RDF.Parsing.Contexts;
using VDS.RDF.Parsing.Handlers;
using VDS.RDF.Parsing.Tokens;
using VDS.RDF.Query;
using VDS.RDF.Query.Expressions;
using VDS.RDF.Writing;
using ListOfBindings = System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, LogicalParticleFilter1.SIProlog.Part>>;
using StringWriter=System.IO.StringWriter;
using VDS.RDF.Writing.Formatting;
using PartList = LogicalParticleFilter1.SIProlog.PartListImpl;
#if MERGED_RDFSTORE
using GraphWithDef = LogicalParticleFilter1.SIProlog.PNode;
#endif
namespace LogicalParticleFilter1
{
//    using GraphWithDef = SIProlog.PNode;
    public static class RDFExtensions
    {
        //static public Dictionary<GraphWithDef.Rule, INode> rule2Node = new Dictionary<GraphWithDef.Rule, INode>();
        static public Dictionary<SIProlog.Rule, INode> rule2Node = new Dictionary<SIProlog.Rule, INode>();

        public static INode instanceTriple(this SIProlog.Rule rule)
        {
            if (rule == null) return null;
            SIProlog.RdfRules ruleCache = rule.rdfRuleCache;
            if (ruleCache != null)
            {
                INode node = ruleCache.RuleNode;
                if (node != null) return node;
            }
            INode saved;
            lock (rule2Node)
            {
                if (rule2Node.TryGetValue(rule, out saved))
                {
                    if (ruleCache != null)
                    {
                        rule.rdfRuleCache.RuleNode = saved;
                    }
                    return saved;
                }
            }
            return null;
        }
        public static int WordCount(this String str)
        {
            return str.Split(new char[] { ' ', '.', '?' },
                             StringSplitOptions.RemoveEmptyEntries).Length;
        }

#if !MERGED_RDFSTORE
        public static SIProlog.RdfRules RdfRuleValue(this SIProlog.Rule prologRule)
        {
            // termVarNames
            SIProlog prolog = SIProlog.CurrentProlog;
            lock (prolog)
            {
                var rr = prologRule.rdfRuleCache;
                if (rr == null)
                {
                    string prologRuleoptHomeMt = prologRule.optHomeMt;
                    SIProlog.PGraph gg = SIProlog.GlobalKBGraph;
                    var kb = prolog.MakeRepositoryKB(prologRuleoptHomeMt).RdfStore;
                    SIProlog.GraphWithDef.FromRule(prologRule, kb.rdfGraph);
                    rr = prologRule.rdfRuleCache;
                }
                return rr;
            }
        }
#endif
    }

    public partial class SIProlog
    {        
        static readonly internal Graph rdfDefinations = new Graph();
        const string rdfDefMT = "rdfGlobalDefsMt";
        public const string TripleName = "triple";
        private GraphWithDef rdfDefSync;
        public CIDictionary<string, GraphWithDef> GraphForMT = new CIDictionary<string, GraphWithDef>(new KeyCase(NormalizeKBName));

        public static string NormalizeKBName(object arg)
        {
            string retVal = KeyCase.NormalizeKeyLowerCaseNoFileExt(arg);
            int retValLength = retVal.Length;
            if (retValLength > 3)
            {
                if (retVal.EndsWith("mt") || retVal.EndsWith("kb")) retVal = retVal.Substring(0, retValLength - 2);
            }
            return retVal;
        }

        public static bool SameKBName(string kb1,string kb2)
        {
            return NormalizeKBName(kb1) == NormalizeKBName(kb2);
        }

        static public string RoboKindURI = "http://cogserver:8123/onto/robokind#";
        public static string RoboKindMtURI = RoboKindURI;// "http://cogserver:8123/mt/";
        public static string RoboKindPrefix = "robokind";
        public static string RoboKindPrefixPrepend = RoboKindPrefix + ":";
        private static readonly CIDictionary<string, PredicateProperty> SharedGlobalPredDefs = new CIDictionary<string, PredicateProperty>(KeyCase.Default);
        public static bool SharedGlobalPredDefsDirty = false;
        private static CIDictionary<string, RDFArgSpec> SharedGlobalArgTypeDefs = new CIDictionary<string, RDFArgSpec>(KeyCase.Default);

        private void defineRDFExtensions()
        {
            Options.InternUris = true;
            Options.LiteralValueNormalization = true;
            Options.LiteralEqualityMode = LiteralEqualityMode.Loose;

            var uriAgainIs = UriFactory.Create(RoboKindURI);
            rdfDefinations.BaseUri = uriAgainIs;
            LoadGraphPrefixes(rdfDefinations);
            rdfDefinations.BaseUri = uriAgainIs;
            rdfDefSync =
                rdfDefSync ??
                //KBGraph.Contains(rdfDefMT) ??
                new GraphWithDef(rdfDefMT, this, rdfDefinations);
            forReaderTripleStoreGraph.BaseUri = uriAgainIs;
            EnsureReaderNamespaces(forReaderTripleStoreGraph);
            var rdfKB = FindOrCreateKB(rdfDefMT);
            rdfKB.SourceKind = ContentBackingStore.RdfMemory;
            rdfDefSync.IncludeRDFUri(new FileInfo("aiml/shared_ke/PrologToRDFConversion.owl").FullName);
            loadKEText(rdfDefMT, FromStream("aiml/shared_ke/argdefs.txt"), false);
            if (DeveloperSanityChecks > 0) Program.RunAllTests(this);
        }

        public void UpdateSharedGlobalPredDefs()
        {
            lock (SharedGlobalPredDefs)
            {
                if (!SharedGlobalPredDefsDirty)
                    return;
                SharedGlobalPredDefsDirty = false;
                foreach (var defs in SharedGlobalPredDefs.Values)
                {
                    foreach (var t in defs.DefinitionalRDFEnsurerd) rdfGraphAssert(rdfDefinations, t, true, true);
                }
            }
        }

        private static void EnsureReaderNamespaces(IGraph graph)
        {
            lock (graph)
            {
                INamespaceMapper nm = graph.NamespaceMap;
                if (!nm.HasNamespace(RoboKindPrefix))
                    nm.Import(rdfDefinations.NamespaceMap);
                EnsureBaseURIMapped(graph);
            }
        }

        private static void EnsureBaseURIMapped(IGraph graph)
        {
            INamespaceMapper nm = graph.NamespaceMap;
            bool hsBlank = nm.HasNamespace("");
            var BaseUri = graph.BaseUri;
            if (!hsBlank)
            {
                if (BaseUri != null)
                {
                    nm.AddNamespace("", BaseUri);
                    return;
                }
                var newIdea = UriFactory.Create(RoboKindURI);
                graph.BaseUri = newIdea;
                nm.AddNamespace("", newIdea);
                return;

            }
            Uri previosuBlankURI = nm.GetNamespaceUri("");
            var previousBlank = previosuBlankURI.AbsoluteUri;
            if (BaseUri != null)
            {
                if (previousBlank != BaseUri.AbsoluteUri) nm.AddNamespace("", BaseUri);
                return;
            }
            var newIdea2 = UriFactory.Create(RoboKindURI);
            nm.AddNamespace("", newIdea2);
        }

        public PNode MakeRepositoryKB(string mt)
        {
            lock (KBGraph) return MakeRepositoryKB_unlocked(mt);
        }
        private PNode MakeRepositoryKB_unlocked(string mt)
        {
            bool newlyCreated;
            var node = MakeRepositoryKB(mt, out newlyCreated);
            if (newlyCreated)
            {
                EverythingPSC = EverythingPSC ?? MakeRepositoryKB("everythingPSC", out newlyCreated);
                BaseKB = BaseKB ?? MakeRepositoryKB("baseKB", out newlyCreated);
                if (node != EverythingPSC && node != BaseKB)
                {
                    EverythingPSC.CreateEdgeTo(node);
                    node.CreateEdgeTo(BaseKB);
                }
                //connectMT(epsc.Id, mt); // 
                //connectMT(mt, basekb.Id); // 
            }
            return node;
        }

        private PNode MakeRepositoryKB(string mt, out bool newlyCreated)
        {
            lock (GraphForMT)
            {
#if MERGED_RDFSTORE
                lock (KBGraph)
#endif
                {
                    GraphWithDef graph;
                    newlyCreated = !GraphForMT.TryGetValue(mt, out graph);
                    if (newlyCreated)
                    {
                        PNode pnode = KBGraph.Contains(mt);
                        if (pnode != null)
                        {
                            GraphForMT[mt] = pnode;
                            newlyCreated = false;
                            return pnode;
                        } 
                        Graph newGraph = new Graph();
                        newGraph.BaseUri = UriFactory.Create(UriOfMt(mt));
                        graph = new GraphWithDef(mt, this, newGraph);
#if MERGED_RDFSTORE
                        KBGraph.AddNode(graph);
#endif
                        // var node = graph.PrologKB;
                    }
                    return graph.PrologKB;
                }
            }
        }

        public void WriteEnumeration<T>(TextWriter writer, IEnumerable<T> triple, Func<T, object> toString)
        {
            writer.WriteLine("<pre>");
            foreach (T t in triple)
            {
                object os = toString(t);
                if (os == null) continue;
                var ts = os.ToString();
                writer.WriteLine(ts);
            }
            writer.WriteLine("</pre>");
        }

        public GraphWithDef FindRepositoryKB(string mt)
        {
            lock (GraphForMT)
            {
                GraphWithDef graph;
                if (!GraphForMT.TryGetValue(mt, out graph))
                {
                    return null;
                }
                return graph;
            }
        }


        #region rdfEndpoint
        public void rdfRemoteEndpointToKB(string endpointURI, string graphKBName, string query, string assertTemplate)
        {
            //Define a remote endpoint
            //Use the DBPedia SPARQL endpoint with the default Graph set to DBPedia
            SparqlRemoteEndpoint endpoint = new SparqlRemoteEndpoint(new Uri(endpointURI));

            var focus = MakeRepositoryKB(graphKBName);
            RdfRules ruleDefs = new RdfRules(focus.RdfStore.rdfGraph);

            StringWriter miniMt = new StringWriter();

            //Use the extension method ExecuteQuery() to make the query against the Graph
            try
            {
                //Object results = g.ExecuteQuery(query);
                //Make a SELECT query against the Endpoint
                SparqlResultSet results = endpoint.QueryWithResultSet(query);
                GetMiniMt(results, assertTemplate, graphKBName, focus.RdfStore, show, null, miniMt, ruleDefs);
            }
            catch (RdfQueryException queryEx)
            {
                //There was an error executing the query so handle it here
                Warn("While endpointURI={0}\n\n{1}", endpointURI, queryEx);
            }
            insertKB(miniMt.ToString(), graphKBName);
        }

        public void rdfImportToKB(IGraph g, string graphKBName, string query, string assertTemplate)
        {
            EnsureReaderNamespaces(g);
            StringWriter miniMt = new StringWriter();
            var repo = MakeRepositoryKB(graphKBName);
            RdfRules ruleDefs = new RdfRules(g);
            //Use the extension method ExecuteQuery() to make the query against the Graph
            try
            {
                Object results = g.ExecuteQuery(query);
                List<Rule> newTriples = new List<Rule>();
                GetMiniMt(results, assertTemplate, graphKBName, repo.RdfStore, show, null, miniMt, ruleDefs);
            }
            catch (RdfQueryException queryEx)
            {
                //There was an error executing the query so handle it here
                Warn(queryEx);
            }
            string miniMtSources = miniMt.ToString();
            insertKB(miniMtSources, graphKBName);
        }

        private void GetMiniMt(object results, string assertTemplate, string graphKBName, GraphWithDef repo, bool show, ICollection<Rule> newRules, TextWriter ruleSources, RdfRules ruleDefs)
        {           
            assertTemplate = assertTemplate ?? "triple($?s$,$?p$,$?o$).\n";
            bool MakeRules = newRules != null && assertTemplate.Trim().EndsWith(".");
            var outMap = new Dictionary<string, string>();
            outMap["s"] = "unknownSubject";
            outMap["p"] = "unknownPredicate";
            outMap["o"] = "unknownObject";
            outMap["mt"] = repo.PlReadble(repo.definations.CreateUriNode(repo.rdfGraph.BaseUri), ruleDefs);

            if (results is SparqlResultSet)
            {
                //SELECT/ASK queries give a SparqlResultSet
                SparqlResultSet rset = (SparqlResultSet)results;
                if (show)
                {
                    ConsoleWriteLine("SparqlResultSet.Count = {0}", rset.Count);
                    ConsoleWriteLine("SparqlResultSet:{0}", rset.ToString());
                }
                foreach (SparqlResult r in rset)
                {
                    //Do whatever you want with each Result
                    if (show)
                    {
                        ConsoleWriteLine("SparqlResult.Count = {0}", r.Count);
                        ConsoleWriteLine("SparqlResult:{0}", r.ToString());
                    }

                    var assertIt = assertTemplate;
                    //Do whatever you want with each Triple
                    foreach (string vname in r.Variables)
                    {
                        INode value = r[vname];
                        string strVal = repo.PlReadble(value, ruleDefs);
                        assertIt = assertIt.Replace("$?" + vname + "$", strVal);
                        if (show) ConsoleWriteLine("BIND: {0} = {1}", vname, strVal);
                    }
                    if (assertIt.Contains("$?s$"))
                    {
                        foreach (KeyValuePair<string, string> map in outMap)
                        {
                            assertIt = assertIt.Replace("$?" + map.Key + "$", map.Value);
                        }
                    }
                    if (MakeRules)
                    {
                        Rule rule = ParseRule(new Tokeniser(assertIt), graphKBName);
                        if (show) ConsoleWriteLine("RULE_IG: {0}", rule);
                        newRules.Add(rule);
                    }
                    else
                    {
                        if (show) ConsoleWriteLine("TRIPLE_IG: {0}", assertIt);
                    }
                    if (ruleSources != null) ruleSources.WriteLine(assertIt);
                }
            }
            else if (results is IGraph)
            {
                //CONSTRUCT/DESCRIBE queries give a IGraph
                IGraph resGraph = (IGraph)results;
                var rset = resGraph.Triples;
                outMap["mt"] =
                    repo.PlReadble(GraphWithDef.CUrlNode(repo.definations, resGraph.BaseUri.AbsoluteUri, false, false),
                                   ruleDefs);
                if (show)
                {
                    ConsoleWriteLine("IGraphResultSet.Count = {0}", rset.Count);
                    ConsoleWriteLine("IGraphResultSet:{0}", rset.ToString());
                }
                foreach (Triple t in rset)
                {
                    var assertIt = assertTemplate;
                    //Do whatever you want with each Triple
                    outMap["s"] = repo.PlReadble(t.Subject, ruleDefs);
                    outMap["p"] = repo.PlReadble(t.Predicate, ruleDefs);
                    outMap["o"] = repo.PlReadble(t.Object, ruleDefs);
                    foreach (KeyValuePair<string, string> map in outMap)
                    {
                        assertIt = assertIt.Replace("$?" + map.Key + "$", map.Value);
                    }
                    if (MakeRules)
                    {
                        Rule rule = ParseRule(new Tokeniser(assertIt), graphKBName);
                        if (show) ConsoleWriteLine("RULE_IG: {0}", rule);
                        newRules.Add(rule);
                    }
                    else
                    {
                        if (show) ConsoleWriteLine("TRIPLE_IG: {0}", assertIt);
                    }
                    if (ruleSources != null) ruleSources.WriteLine(assertIt);
                }
            }
            else
            {
                //If you don't get a SparqlResutlSet or IGraph something went wrong 
                //but didn't throw an exception so you should handle it here
                if (results == null) throw ErrorBadOp("ERROR: no Results From NULL Query Object for " + graphKBName);
                throw ErrorBadOp("ERROR: Cant how understand " + results.GetType() + " " + results + " to import to " +
                                 graphKBName);
            }
        }

        public void pushRulesToGraph(string mt, GraphWithDef rdfGraphWithDefs, bool includeInherited)
        {
            // Possibly called by the Sparql endpoint before servicing a query
            // Is there anything we want to update rdfGraph with ?
            var bingingsList = new ListOfBindings();
            askQuery(ParseBody("triple(S,P,O)", mt), mt, false, bingingsList, null);
            bool useTripeQuery = true;
            if (bingingsList == null || bingingsList.Count <= 0)
            {
                useTripeQuery = false;
                var triples = findVisibleKBRules(mt, new List<string>(), includeInherited);
                foreach (Rule rule in triples)
                {
                    try
                    {
                        rdfGraphWithDefs.AddRuleToRDF(rule);
                    }
                    catch (Exception e)
                    {
                        Warn(e);
                    }
                }
            }

            var rdfGraph = rdfGraphWithDefs.rdfGraph;

            if (!useTripeQuery) return;
            var newTriples = new RdfRules(rdfGraphWithDefs.definations);
            EnsureGraphPrefixes(rdfGraph);
            foreach (Dictionary<string, Part> bindings in bingingsList)
            {
                Part psubj = bindings["S"];
                Part ppred = bindings["P"];
                Part pobj = bindings["O"];
                INode subj = GraphWithDef.PartToRdf(psubj, newTriples);
                INode pred = GraphWithDef.PartToRdf(ppred, newTriples);
                INode obj = GraphWithDef.PartToRdf(pobj, newTriples);
                if (subj is ILiteralNode)
                {
                    Warn("Subj was a literal (not supported) [{0} {1} {2}]", subj, pred, obj);
                    continue;
                }
                if (!(pred is IUriNode))
                {
                    Warn("Pred was not a uri (not supported) [{0} {1} {2}]", subj, pred, obj);
                    continue;
                }
                //foreach (string k in bindings.Keys)
                //{
                rdfGraphAssert(rdfGraph,MakeTriple(subj,pred,obj));
                //string rdfLine = String.Format(@"<{0}> <{1}> <{2}> .", bindings["S"].ToString(), bindings["P"].ToString(), bindings["O"].ToString());
                //StringParser.Parse(rdfGraph, rdfLine);
                // }
            }
            newTriples.RequirementsMet = true;
            newTriples.AssertTriples(rdfGraphWithDefs.definations, true, true);
        }

        static string UriOfMt(string plMt)
        {
            return RoboKindURI.TrimEnd('#', '/') + "/" + plMt + "#";
        }


        public class RdfRules
        {
            ///<summary>
            ///</summary>
            public string _aInfo
            {
                get
                {
                    try
                    {
                        return ToString();
                    }
                    catch (Exception)
                    {
                        return ToString();
                    }
                }
            }

            private bool _requirementsMet = true;
            private HashSet<INode> RequiredAnteceedantVars = new HashSet<INode>();
            public IGraph ContainingGraph;
            public IGraph def
            {
                get
                {
                    return ContainingGraph ?? _graph;
                }
            }
            private INode _ruleNode;
            public List<INode> Subjects = new List<INode>();
            public List<Triple> Requirements = new List<Triple>();
            public List<Triple> Producing = new List<Triple>();
            public List<Triple> Consequences = new List<Triple>();

            IGraph _graph;
            public override string ToString()
            {
                INamespaceMapper graphNamespace = _graph.NamespaceMap;
                StringWriter sw = new StringWriter();
                Graph ig = new Graph();
                ig.NamespaceMap.Import(graphNamespace);
                AssertTriples(ig, false, true);
                sw.WriteLine("# subjs= {0} metreq={1} ", Subjects.Count, RequirementsMet);
                sw.WriteLine("BEGIN: ");
                DumpTriplesPlain(ig.Triples, sw, " {0}", ig);
                sw.WriteLine("EOT.");
                sw.WriteLine("TOTRIPLES: ");
                DumpTriplesPlain(ToTriples, sw, " {0}", ig);
                sw.WriteLine("EOT.");
                //WriteGraph(sw, ig, "rdfs triples", true, false);
                return sw.ToString();
            }

            public RdfRules(IGraph graph)
            {
                _graph = graph;
                EnsureReaderNamespaces(graph);
                //Producing = Requirements;
            }

            public void AddRequirement(Triple triple)
            {
                if (Contains(triple))
                {
                    return;
                }
               // CheckTriple(triple); ;
                _requirementsMet = false;
                Requirements.Add(triple);
            }
            public void AddProducing(Triple triple)
            {
                if (Contains(triple))
                {
                    return;
                }
                if (triple.Subject.NodeType == NodeType.Variable)
                {
                    AddRequirement(triple);
                    return;
                }
                if (triple.Object.NodeType == NodeType.Variable)
                {
                    AddRequirement(triple);
                    return;
                }
                CheckTriple(triple);               
                Producing.Add(triple);
            }
            public void AddConsequent(Triple triple)
            {
                if (Contains(triple))
                {
                    return;
                }
                if (triple.Object.NodeType == NodeType.Variable)
                {
                    _requirementsMet = false;
                    RequiredAnteceedantVars.Add(triple.Object);
                }
                if (triple.Subject.NodeType == NodeType.Variable)
                {
                    _requirementsMet = false;
                    RequiredAnteceedantVars.Add(triple.Subject);
                }

                _requirementsMet = false;
                Consequences.Add(triple);
            }

            private bool Contains(Triple triple)
            {
                if (Consequences.Contains(triple)) return true;
                if (Producing.Contains(triple)) return true;
                if (Requirements.Contains(triple)) return true;
                return false;
            }

            public bool RequirementsMet
            {
                get
                {
                    if (_requirementsMet) return true;
                    if (_ruleNode == null)
                    {
                        return ToTriples.Count() == Producing.Count;
                    }
                    if (Requirements.Count == 0) return true;
                    return false;
                }
                set
                {
                    _requirementsMet = value;
                }
            }
            public INode RuleNode
            {
                get
                {
                    if (_ruleNode == null)
                    {
                        ///_ruleNode = def.CreateBlankNode();
                    }
                    return _ruleNode;
                }
                set
                {
                    if (value.NodeType != NodeType.Blank)
                    {
                        Warn("Rule node should be a Bnode.. not " + value);
                    }
                    _ruleNode = value;
                }
            }
            public IEnumerable<Triple> ToTriples
            {
                get
                {
                    ICollection<Triple> temp = new HashSet<Triple>();
                    if (Requirements.Count > 0)
                    {
                        if (Consequences.Count > 0)
                        {
                            temp.Add(GraphWithDef.CreateImplication(_graph, Requirements, Consequences));
                        }
                        else
                        {
                            temp.Add(GraphWithDef.CreateImplication(_graph, Requirements, Producing));
                            return temp;
                        }
                    }
                    else
                    {
                        bool generateAnteceedant = RequiredAnteceedantVars.Count > 0;
                        if (Consequences.Count > 0)
                        {
                            if (generateAnteceedant)
                            {
                                var p = new List<Triple>(Producing);
                                var c = new List<Triple>(Consequences);
                                if (p.Count == 0)
                                {
                                    foreach (Triple consequence in c)
                                    {
                                        if (ContainsVariable(consequence))
                                        {
                                            p.Add(consequence);
                                        }
                                    }
                                    foreach (Triple consequence in p)
                                    {
                                        c.Remove(consequence);
                                    }
                                }
                                if (p.Count > 0)
                                {
                                    if (c.Count == 0)
                                    {
                                        Warn("cant have Consequent");
                                    }
                                    else
                                    {
                                        temp.Add(GraphWithDef.CreateImplication(_graph, p, c));
                                        foreach (var t in Producing)
                                        {
                                            temp.Add(t);
                                        }
                                        return temp;
                                    }
                                }
                                else
                                {
                                    Warn("cant precondition Consequent");
                                }
                            }
                            else
                            {
                                foreach (Triple consequence in Consequences)
                                {
                                    temp.Add(consequence);
                                }
                            }
                        }
                        else
                        {
                            if (Producing.Count == 0)
                            {
                                return temp;
                            }
                        }
                    }
                    foreach (var t in Producing)
                    {
                        temp.Add(t);
                    }                   
                    return temp;
                }
            }

            private static bool ContainsVariable(Triple triple)
            {
                if (triple.Object.NodeType == NodeType.Variable)
                {
                    return true;
                }
                if (triple.Subject.NodeType == NodeType.Variable)
                {
                    return true;
                }
                if (triple.Predicate.NodeType == NodeType.Variable)
                {
                    return true;
                }
                return false;
            }

            public string AssertTriples(IGraph kb, bool checkWff, bool saveToKB)
            {
                var ToTriples = this.ToTriples;

                string bad = "";
                if (saveToKB && !RequirementsMet && checkWff)
                {
                    bad = "Meet requirements please! ";
                    saveToKB = false;
                }
                if (checkWff)
                {
                    bad += Check(kb);
                }
                bool wasGood = (string.IsNullOrEmpty(bad));
                if (wasGood && saveToKB)
                {
                    foreach (Triple triple in ToTriples)
                    {
                        bad += rdfGraphAssert(kb, triple, checkWff, true);
                    }
                }
                wasGood = string.IsNullOrEmpty(bad);
                if (wasGood)
                {
                    if (saveToKB)
                    {
                        ContainingGraph = kb;
                    }
                    return "";
                }
               // bad += " " + ToString();
                if (saveToKB)
                {
                    throw ErrorBadOp(bad);
                }
                Warn(bad);
                return bad;
            }
            public string Check(IGraph kb)
            {
                string bad = "";
                foreach (Triple triple in ToTriples)
                {
                    bad += rdfGraphAssert(kb, triple, true, false);
                }
                if (string.IsNullOrEmpty(bad)) return null;
                return bad;
            }

            internal void AddSubject(INode rdf)
            {
                if (rdf == null) return;
                if (!Subjects.Contains(rdf)) Subjects.Add(rdf);
            }

            public void Clear()
            {
                Producing.Clear();
                Consequences.Clear();
                Requirements.Clear();
            }

            public void AddRequirement(INode s, string sp, Part o)
            {
                AddRequirement(MakeTriple(s,
                           GraphWithDef.PredicateToProperty(sp),
                           GraphWithDef.PartToRdf(o, this), false));
            }
        }

        public static Triple MakeTriple(INode s, INode p, INode o)
        {
            return MakeTriple(s, p, o, true);
        }
        public static Triple MakeTriple(INode s, INode p, INode o, bool toplevel)
        {
            IGraph sGraph = o.Graph;
            if (!ReferenceEquals(sGraph, p.Graph))
            {
                p = p.CopyNode(sGraph, true);
            }
            if (!ReferenceEquals(sGraph, o.Graph))
            {
                o = o.CopyNode(sGraph, true);
            }
            if (!ReferenceEquals(sGraph, s.Graph))
            {
                s = s.CopyNode(sGraph, true);
            }
            Triple newTriple = new Triple(s, p, o);
            string warn0 = "";
            Action<string> warn = s0 =>
                                      {
                                          warn0 += s0 + " \n";
                                      };
            if (p.NodeType != NodeType.Uri)
            {
                warn("pred " + p + " is not URI");
            }
            if (s.NodeType == NodeType.GraphLiteral)
            {
                if (toplevel)
                {
                    IUriNode uri = p as IUriNode;
                    if (uri == null || !uri.Uri.ToString().Contains("log"))
                    {
                        warn("s = not logical (" + s + ")");
                    }
                }
            }
            else
            {
                if (s is ILiteralNode)
                {
                    warn("s = LiteralNode (" + s + ")");
                }
                if (toplevel && !GraphWithDef.CanBeSubjectNode(s))
                {
                    warn("s = !CanBeSubjectNode (" + s + ")");
                }
            }
            if (p is ILiteralNode)
            {
                warn("p = LiteralNode (" + p + ")");
            }
            if (warn0 != "")
            {
                warn("bad triple =" + newTriple);
                Warn(warn0);
            }
            return newTriple;
        }
        static public bool rdfGraphAssert(IGraph rdfGraph, Triple triple)
        {
            string bad = rdfGraphAssert(rdfGraph, triple, true, true);
            if (!string.IsNullOrEmpty(bad)) Warn(bad);
            return string.IsNullOrEmpty(bad);
        }

        static public string rdfGraphAssert(IGraph rdfGraph, Triple triple, bool checkWff, bool saveToKB)
        {
            if (checkWff)
            {
                string bad = CheckTriple(triple);
                if (!string.IsNullOrEmpty(bad))
                {
                    return bad;
                }
            }
            if (!saveToKB) return "";
            lock (rdfGraph)
            {
                if (rdfGraph.ContainsTriple(triple)) return null;
                rdfGraph.Assert(triple);
                return "";
            }
        }

        public static string CheckTriple(Triple triple)
        {
            foreach (INode node in triple.Nodes)
            {
                if (node.NodeType == NodeType.Variable)
                {
                    string bad = string.Format("Bad {0} found in triple {1}", node.NodeType, triple);
                    return bad;
                }
            }
            return "";
        }

        public void refreshRDFGraphOLD()
        {
            var rdfGraphWithDefs = MakeRepositoryKB("rdfMT");
            var rdfGraph = rdfGraphWithDefs.RdfStore.rdfGraph;
            EnsureGraphPrefixes(rdfGraph);
            // Possibly called by the Sparql endpoint before servicing a query
            // Is there anything we want to update rdfGraph with ?
            var bingingsList = new ListOfBindings();
            string mt = "spindleMT";
            askQuery(ParseBody("triple(S,P,O)", mt), mt, true, bingingsList, null);
            RdfRules newTriples = new RdfRules(rdfGraph);
            foreach (var bindings in bingingsList)
            {
                //foreach (string k in bindings.Keys)
                //{
                rdfGraphAssert(rdfGraph,
               MakeTriple(GraphWithDef.PartToRdf(bindings["S"], newTriples),
                          GraphWithDef.PartToRdf(bindings["P"], newTriples),
                          GraphWithDef.PartToRdf(bindings["O"], newTriples)));

                string rdfLine = String.Format(@"<robokind:{0}> <robokind:{1}> <robokind:{2}> .", bindings["S"].ToString(), bindings["P"].ToString(), bindings["O"].ToString());
                StringParser.Parse(rdfGraph, rdfLine);
                // }
            }
            newTriples.AssertTriples(rdfGraphWithDefs.definations, true, true);
        }
        public IGraph getRefreshedRDFGraph(string queryMT)
        {
            var graph = MakeRepositoryKB(queryMT);
            ensureCompiled(graph, ContentBackingStore.RdfMemory);
            return graph.RdfStore.rdfGraph;
        }

        public static void EnsureGraphPrefixes(IGraph graph)
        {
            var nm = graph.NamespaceMap;
            if (nm.HasNamespace(RoboKindPrefix))
            {
                return;
            }
            EnsureReaderNamespaces(graph);
        }

        public static void LoadGraphPrefixes(IGraph graph)
        {
            var nm = graph.NamespaceMap;
            if (nm.HasNamespace(RoboKindPrefix))
            {
                return;
            }
            string s =
                @"
@prefix : <#> .
@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .
@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .
@prefix daml: <http://www.daml.org/2001/03/daml+oil#> .
@prefix log: <http://www.w3.org/2000/10/swap/log.n3#> .
@prefix foaf: <http://xmlns.com/foaf/0.1/> .
@prefix owl: <http://www.w3.org/2002/07/owl#> .
@prefix virtrdf: <http://www.openlinksw.com/schemas/virtrdf#> .
@prefix sswap: <http://sswapmeet.sswap.info/sswap/#> .
@prefix sioc: <http://rdfs.org/sioc/ns#> .
@prefix sioct: <http://rdfs.org/sioc/types#> .
@prefix atom: <http://atomowl.org/ontologies/atomrdf#> .
@prefix dc: <http://purl.org/dc/elements/1.1/> .
@prefix dct: <http://purl.org/dc/terms/> .
@prefix skos: <http://www.w3.org/2004/02/skos/core#> .
@prefix geo: <http://www.w3.org/2003/01/geo/wgs84_pos#> .
@prefix wikiont: <http://sw.deri.org/2005/04/wikipedia/wikiont.owl#> .
@prefix aowl: <http://atomowl.org/ontologies/atomrdf#> .
@prefix v: <http://www.openlinksw.com/schemas/drupal_v#> .
@prefix sd: <http://www.w3.org/ns/sparql-service-description#> .
@prefix dbpprop: <http://dbpedia.org/property/> .
@prefix dbpedia-owl:	<http://dbpedia.org/ontology/> .
@prefix dc: <http://purl.org/dc/elements/1.1/> .
@prefix grddl: <http://www.w3.org/2003/g/data-view#> .
@prefix xml: <http://www.w3.org/XML/1998/namespace> .
@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .
@prefix siprolog: <http://cogbotserver:8123/siprolog#> .
@prefix robokind: <" +
                RoboKindURI + @"> .
";
            StringParser.Parse(graph, s, new Notation3Parser());
            string ss =
                @"
fn http://www.w3.org/2005/xpath-functions
gmlxbt http://www.opengis.net/gml/3.3/xbt
sch http://www.ascc.net/xml/schematron
# gml http://www.opengis.net/gml/3.2
gml http://www.opengis.net/gml/_
gmd http://www.isotc211.org/2005/gmd
xlink http://www.w3.org/1999/xlink
xsl  http://www.w3.org/1999/XSL/Transform 
rdf  http://www.w3.org/1999/02/22-rdf-syntax-ns# 
p3q  http://www.w3.org/2004/01/rdxh/p3q-ns-example 
p3qr http://www.example.org/P3Q-rdf# 
p3dr http://www.example.org/TR/P3P/base# 
ont  http://www.daml.org/2001/03/daml+oil# 
s http://schema.org/
xsd http://www.w3.org/2001/XMLSchema#
eco http://www.ebusiness-unibw.org/ontologies/eclass/5.1.4/
gr http://purl.org/goodrelations/v1#
dc http://purl.org/dc/elements/1.1/
ao	http://purl.org/ao/core/
aoa	http://purl.org/ao/annotea/
aof	http://purl.org/ao/foaf/
aold	http://biotea.ws/ontologies/aold/
aos	http://purl.org/ao/selectors/
aot	http://purl.org/ao/types/
bibo	http://purl.org/ontology/bibo/
bif	bif:
bio2rdf_mesh	http://bio2rdf.org/ns/mesh#
bio2rdf_ns	http://bio2rdf.org/ns/bio2rdf#
chebi	http://purl.obolibrary.org/obo/CHEBI_
cnt	http://www.w3.org/2011/content#
dawgt	http://www.w3.org/2001/sw/DataAccess/tests/test-dawg#
dbpedia	http://dbpedia.org/resource/
dbpprop	http://dbpedia.org/property/
dc	http://purl.org/dc/elements/1.1/
dcterms	http://purl.org/dc/terms/
doco	http://purl.org/spar/doco/
fma	http://purl.org/obo/owl/FMA#FMA_
fn	http://www.w3.org/2005/xpath-functions/#
foaf	http://xmlns.com/foaf/0.1/
geo	http://www.w3.org/2003/01/geo/wgs84_pos#
go	http://purl.org/obo/owl/GO#GO_
gw_property	http://genewikiplus.org/wiki/Special:URIResolver/Property-3A
gw_wiki	http://genewikiplus.org/wiki/Special:URIResolver/
icd9	http://purl.bioontology.org/ontology/ICD9-9/
math	http://www.w3.org/2000/10/swap/math#
mddb	http://purl.bioontology.org/ontology/MDDB/
meddra	http://purl.bioontology.org/ontology/MDR/
medline	http://purl.bioontology.org/ontology/MEDLINEPLUS/
mesh	http://purl.org/commons/record/mesh/
mf	http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#
mged	http://mged.sourceforge.net/ontologies/MGEDOntology.owl#
ncbitaxon	http://purl.org/obo/owl/NCBITaxon#NCBITaxon_
ncithesaurus	http://ncicb.nci.nih.gov/xml/owl/EVS/Thesaurus.owl#
nddf	http://purl.bioontology.org/ontology/NDDF/
ndfrt	http://purl.bioontology.org/ontology/NDFRT/
obi	http://purl.obolibrary.org/obo/OBI_
obo	http://www.geneontology.org/formats/oboInOwl#
omim	http://purl.bioontology.org/ontology/OMIM/
owl	http://www.w3.org/2002/07/owl#
pav	http://purl.org/swan/pav/provenance/
po	http://purl.bioontology.org/ontology/PO/
product	http://www.buy.com/rss/module/productV2/
protseq	http://purl.org/science/protein/bysequence/
prov	http://www.w3.org/ns/prov#
pw	http://purl.org/obo/owl/PW#PW_
rdf	http://www.w3.org/1999/02/22-rdf-syntax-ns#
rdfa	http://www.w3.org/ns/rdfa#
rdfdf	http://www.openlinksw.com/virtrdf-data-formats#
rdfs	http://www.w3.org/2000/01/rdf-schema#
sc	http://purl.org/science/owl/sciencecommons/
scovo	http://purl.org/NET/scovo#
sioc	http://rdfs.org/sioc/ns#
skos	http://www.w3.org/2004/02/skos/core#
snomed	http://purl.bioontology.org/ontology/SNOMEDCT/
sql	sql:
swivt	http://semantic-mediawiki.org/swivt/1.0#
symptom	http://purl.org/obo/owl/SYMP#SYMP_
taxonomy	http://www.uniprot.org/taxonomy/
umls	http://berkeleybop.org/obo/UMLS:
uniprot	http://purl.uniprot.org/core/
vcard	http://www.w3.org/2001/vcard-rdf/3.0#
vcard2006	http://www.w3.org/2006/vcard/ns#
virtcxml	http://www.openlinksw.com/schemas/virtcxml#
virtrdf	http://www.openlinksw.com/schemas/virtrdf#
void	http://rdfs.org/ns/void#
xf	http://www.w3.org/2004/07/xpath-functions
xml	http://www.w3.org/XML/1998/namespace
xsd	http://www.w3.org/2001/XMLSchema#
xsl10	http://www.w3.org/XSL/Transform/1.0
xsl1999	http://www.w3.org/1999/XSL/Transform
xslwd	http://www.w3.org/TR/WD-xsl
xsp	http://www.owl-ontologies.com/2005/08/07/xsp.owl#
yago	http://dbpedia.org/class/yago/
";

            foreach (string s00 in ss.Split('\n', '\r'))
            {
                if (string.IsNullOrEmpty(s00)) continue;
                var s0 = s00.Replace('\t', ' ').Trim();
                if (s0.StartsWith("#")) continue;
                if (string.IsNullOrEmpty(s0)) continue;
                int spc = s0.IndexOf(' ');
                string prefix = s0.Substring(0, spc).Trim().TrimEnd(' ', ':');
                string uri = s0.Substring(spc).Trim();
                if (nm.HasNamespace(prefix))
                {
                    var prev = nm.GetNamespaceUri(prefix).ToString();
                    if (prev != uri)
                    {
                        if (uri.Length < prev.Length)
                        {
                            continue;
                        }
                    }
                }
                nm.AddNamespace(prefix, new Uri(uri));
            }
        }

        public void mtest()
        {

            IGraph g = new Graph();
            g.BaseUri = UriFactory.Create(RoboKindURI);

            IUriNode dotNetRDF = g.CreateUriNode(UriFactory.Create("http://www.dotnetrdf.org"));
            IUriNode says = g.CreateUriNode(UriFactory.Create("http://example.org/says"));
            ILiteralNode helloWorld = g.CreateLiteralNode("Hello World");
            ILiteralNode bonjourMonde = g.CreateLiteralNode("Bonjour tout le Monde", "fr");

            rdfGraphAssert(g, MakeTriple(dotNetRDF, says, helloWorld));
            rdfGraphAssert(g, MakeTriple(dotNetRDF, says, bonjourMonde));

            foreach (Triple t in g.Triples)
            {
                ConsoleWriteLine(t.ToString());
                ConsoleWriteLine("TRIPLE: triple(\"{0}\",\"{1}\",\"{2}\").", t.Subject.ToString(),
                                 t.Predicate.ToString(), t.Object.ToString());
            }

            NTriplesWriter ntwriter = new NTriplesWriter();
            ntwriter.Save(g, "HelloWorld.nt");

            RdfXmlWriter rdfxmlwriter = new RdfXmlWriter();
            rdfxmlwriter.Save(g, "HelloWorld.rdf");

            MakeRepositoryKB("testRDF").SourceKind = ContentBackingStore.Prolog;
            if (DeveloperSanityChecks < 2) return;
            rdfImportToKB(g,
                          "testRDF",
                          "SELECT * WHERE { ?s ?p ?o }",
                          null);
            foreach (var nameAndEndp in
                new[]
                    {
                        //new[] {"http://budapest.rkbexplorer.com/sparql"},
                        new[] {"dbpedia", "http://dbpedia.org/sparql"},
                        //   new[] {"josekiBooks", "http://cogbotserver:2020"},
                        //  new[] {"cogPoint", "http://cogbotserver:8181"},
                        //new[] {"hebis", "http://lod.hebis.de/sparql"},
                    })
            {
                string prefix = nameAndEndp[0];
                string endp = nameAndEndp[1];
                CreateTestTriangle(prefix, endp);
            }
            return;
        }

        private void CreateTestTriangle(string prefix, string endp)
        {
            var prolog100Mt = prefix + "Prolog100KB";
            PNode kb1 = MakeRepositoryKB(prolog100Mt);
            kb1.SourceKind = ContentBackingStore.Prolog;
            rdfRemoteEndpointToKB(endp,
                                  prolog100Mt,
                                  "SELECT DISTINCT ?o WHERE { ?s a ?o } LIMIT 100",
                                  "isa($?o$,{http://www.w3.org/2002/07/owl#Class}).\n");

            PNode kb2 = MakeRepositoryKB(prefix + "RdfServerURI");
            kb2.SourceKind = ContentBackingStore.RdfServerURI;
            kb2.Repository = endp;
            PNode kb3 = MakeRepositoryKB(prefix + "RdfMemory");
            kb3.SourceKind = ContentBackingStore.RdfMemory;
            kb3.Repository = endp;
            testKBConsitancy(kb1);
            testKBConsitancy(kb2);
            testKBConsitancy(kb3);
        }

        private void testKBConsitancy(PNode node)
        {
            TextWriter sw = DLRConsole.Out;
            WriteMtInfo(sw, node.Id, "" + node.RdfStore.rdfGraph.BaseUri, false);
        }

        #endregion
        static readonly Graph forReaderTripleStoreGraph = new Graph();
        static TurtleParser forReaderTurtleParser = new TurtleParser();
        [ThreadStatic]
        private string tl_ServerRoot;
        [ThreadStatic]
        private string tl_mt;
        [ThreadStatic]
        private string tl_rule_mt;
        private string curKB
        {
            get
            {
                return tl_mt;
            }
            set
            {
                tl_mt = value;
            }
        }
        [ThreadStatic]
        private TextWriter tl_writer;

        public static PNode BaseKB;
        public static PNode EverythingPSC;

        public static INode ToValueNode(INode obj)
        {
            var _name = (INode)obj;
            if (!(_name is IValuedNode))
            {
                var vnode = _name.AsValuedNode();
                if (ReferenceEquals(null, vnode))
                {
                }
                else
                {
                    if (!ReferenceEquals(vnode, _name))
                    {
                        _name = vnode;
                    }
                }
            }
            return _name;
        }

        public static INode GetValuedNode(string s)
        {
            try
            {
                var t = GetNode(s);
                if (t == null) return t;
                try
                {
                    return t.AsValuedNode();
                }
                catch (Exception e)
                {
                    return t;
                }
            }
            catch (Exception e)
            {
                Warn("GetValuedNode '{0}' caused: {1}", s, e);
                return null;
            }
        }

        public static INode GetNode(string s)
        {
            //var obj = TryParseObject(s, (INodeFactory) rdfDefinations);
            //if (obj != null) return obj;

            var forReaderTripleStore = SIProlog.forReaderTripleStoreGraph;
            lock (forReaderTripleStore)
            {
                forReaderTripleStore.Clear();
                EnsureReaderNamespaces(forReaderTripleStore);
                //forReaderTurtleParser.Load(forReaderTripleStore, "{ 1 1 " + s + " }");
                try
                {
                    if ((s.Contains("/") || s.Contains(":")) && !s.StartsWith("<")) s = "<" + s + ">";
                    StringParser.Parse(forReaderTripleStore, "<http://example.org/a1> <http://example.org/a1> " + s + " . ");
                    var t = forReaderTripleStore.Triples.First().Object;
                    return t;
                }
                catch (Exception e)
                {
                    Warn("GetValuedNode '{0}' caused: {1}", s, e);
                }
                return null;
            }
        }
        static private INode TryParseObject(string s, INodeFactory rdfHandler)
        {
            try
            {
                if (s.Contains("/")) return null;
                StringReader input = new StringReader(s);
                AnyHandler handler = new AnyHandler();
                bool trace = tl_spy_prolog_reader;
                var context = new TokenisingParserContext(handler, new Notation3Tokeniser(input),
                                                                            TokenQueueMode.
                                                                                SynchronousBufferDuringParsing, trace,
                                                                            trace);
                IToken objToken = context.Tokens.Dequeue();
                String dt;

                //Discard Comments
                do
                {
                    rdfHandler = rdfHandler ?? context.Handler;

                    switch (objToken.TokenType)
                    {
                        case Token.BLANKNODE:
                            return rdfHandler.CreateBlankNode();
                        case Token.BLANKNODEWITHID:
                            return rdfHandler.CreateBlankNode(objToken.Value.Substring(2));
                        case Token.URI:
                        case Token.QNAME:
                            return rdfHandler.CreateUriNode(UriFactory.Create(objToken.Value));
                        case Token.LITERALWITHDT:
                            dt = ((LiteralWithDataTypeToken)objToken).DataType;
                            dt = dt.Substring(1, dt.Length - 2);
                            return rdfHandler.CreateLiteralNode(objToken.Value, UriFactory.Create(dt));
                        case Token.LITERALWITHLANG:
                            return rdfHandler.CreateLiteralNode(objToken.Value, ((LiteralWithLanguageSpecifierToken)objToken).Language);
                        case Token.LITERAL:
                            IToken next = context.Tokens.Peek();
                            //Is there a Language Specifier or Data Type?
                            if (next.TokenType == Token.LANGSPEC)
                            {
                                context.Tokens.Dequeue();
                                return rdfHandler.CreateLiteralNode(objToken.Value, next.Value);
                            }
                            else if (next.TokenType == Token.URI)
                            {
                                context.Tokens.Dequeue();
                                return rdfHandler.CreateLiteralNode(objToken.Value, UriFactory.Create(Tools.ResolveUriOrQName(next, context.Namespaces, context.BaseUri)));
                            }
                            else
                            {
                                return rdfHandler.CreateLiteralNode(objToken.Value);
                            }
                        case Token.BOF:
                            objToken = context.Tokens.Dequeue();
                            continue;
                            break;
                        case Token.COMMENT:
                            //Discardable Tokens
                            objToken = context.Tokens.Dequeue();
                            continue;
                        default:
                            throw ErrorBadOp("Unexpected Token '" + objToken.GetType().ToString() + "' encountered, expected a Blank Node, Literal or URI for the Object of a Triple", objToken);
                    }
                } while (true);
            }
            catch (Exception e)
            {

            }
            return null;
        }
        public partial class Rule
        {
            public RdfRules rdfRuleCache;

            public bool SameClause(Rule rule)
            {
                var vars = new Dictionary<string,string>();
                if (!head.SameClause(rule.head, vars))
                {
                    return false;
                }
                int partLen = this.bodyLen;
                int partLen2 = rule.bodyLen;
                if (partLen2 != partLen) return false;
                if (partLen == 0) return true;
                return body.plist.SameClause(rule.body.plist, vars);
            }

            protected int bodyLen
            {
                get
                {
                    if (body == null) return 0;
                    if (IsBodyAlwaysTrue(body.plist)) return 0;
                    return body.plist.Arity;
                }
            }
        }

        public class PredicateProperty
        {
            public string name;
            public int arity;
            public string classname;
            public string keyname;
            public int instanceNumber = 1;
            static public int varNumber = 1;
            public readonly Dictionary<int, RDFArgSpec> argDefs;
            public INode classNode;
            public string assertionMt;
            public readonly List<Triple> DefinitionalRDF = new List<Triple>();
            public List<Triple> DefinitionalRDFEnsurerd
            {
                get
                {
                    if (DefinitionalRDF.Count == 0)
                    {
                        RdfDocumentPred(rdfDefinations);
                    }
                    return DefinitionalRDF;
                }
            }


            /// <summary>
            /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </summary>
            /// <returns>
            /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </returns>
            /// <filterpriority>2</filterpriority>
            public override string ToString()
            {
                return StructToString(this);
            }
            public string AToString
            {
                get { return ToString(); }
            }

            public int GetArgNumForName(string argName)
            {
                foreach (KeyValuePair<int, RDFArgSpec> def in argDefs)
                {
                    if (def.Value.NameMatches(argName)) return def.Key;
                }
                throw ErrorBadOp(argName + " for " + this);

            }

            public PredicateProperty(int arity1)
            {
                arity = arity1;
                argDefs = new Dictionary<int, RDFArgSpec>(arity1 + 1);
            }


            public void WriteHtmlInfo(TextWriter writer)
            {
                writer.WriteLine("<pre>");
                writer.WriteLine("<strong><font color='green'>{0}</font></strong>", name + "/" + arity);
                foreach (KeyValuePair<int, RDFArgSpec> def in argDefs)
                {
                    writer.WriteLine(" Arg " + def.Key + " = " + def.Value.ArgNameInfo);
                }
                DumpTriplesPlain(DefinitionalRDFEnsurerd, writer, "{0}", rdfDefinations);
                writer.WriteLine("</pre>");
            }

            public void RdfDocumentPred(Graph rdef)
            {
                {
                    classNode = classNode ?? GraphWithDef.C(rdef, RoboKindURI + classname);
                    DefinitionalRDF.Add(MakeTriple(classNode, GraphWithDef.InstanceOf, GraphWithDef.PrologPredicateClass));
                    for (int i = 0; i < arity; i++)
                    {
                        RDFArgSpec adef = GraphWithDef.GetAdef(this, i + 1, false);
                        if (adef == null)
                        {
                            adef = GraphWithDef.GetAdef(this, i + 1, true);
                        }
                        var subj = adef.GetRefNode(rdfDefinations);
                        DefinitionalRDF.Add(MakeTriple(subj, GraphWithDef.InstanceOf, GraphWithDef.PrologPredicate));
                        DefinitionalRDF.Add(MakeTriple(subj, GraphWithDef.C(rdef, "rdfs:domain"), classNode));
                        DefinitionalRDF.Add(MakeTriple(subj, GraphWithDef.C(rdef, "rdfs:range"),
                                                       GraphWithDef.C(rdef, "rdfs:Literal")));
                        var localArgTypes = GraphWithDef.localArgTypes;
                        lock (localArgTypes) if (!localArgTypes.Contains(adef)) localArgTypes.Add(adef);
                    }
                    if (PNode.QueryPredicateInfo(name, "prologMappingType") == "DirectOneToOneMapping")
                    {
                    }
                }

            }
        }

        public class RDFArgSpec
        {
            public RDFArgSpec(int hintArgNum1Based)
            {
                SharedGlobalPredDefsDirty = true;
                argNumHint = hintArgNum1Based;
            }
            private string predicateArgName = "_";
            public INode predicateNode;
            public int argNumHint = -1;
            public HashSet<string> argNames = new HashSet<string>();
            public List<PredicateProperty> PredicateProperties = new List<PredicateProperty>();
            public string ArgNameInfo
            {
                get
                {
                    return predicateArgName;
                }
            }
            //public string assertionMt;
            public INode GetRefNode(IGraph def)
            {
                if (predicateArgName.EndsWith("_"))
                {
                    predicateArgName += argNumHint;
                    //Warn("Poorly named prolog argument spec will make a pooly named RDF predicate! " + this);
                    
                }
                return predicateNode ??
                       (predicateNode = GraphWithDef.C(def, GraphWithDef.AsURIString(predicateArgName)));
            }
            /// <summary>
            /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </summary>
            /// <returns>
            /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </returns>
            /// <filterpriority>2</filterpriority>
            public override string ToString()
            {
                return StructToString(this);
            }
            public string AToString
            {
                get { return ToString(); }
            }
            public void AddDomainType(PredicateProperty property, int argNumber1Based)
            {
                lock(PredicateProperties)
                {
                    PredicateProperties.Add(property);
                }
                string functor = property.name;
                if (!predicateArgName.ToLower().StartsWith(functor.ToLower()))
                {
                    predicateArgName = functor + "_" + predicateArgName.TrimStart("_".ToCharArray());
                }
                //AddRangeTypeName("_Arg" + argNumber1Based);
            }

            public void AddRangeTypeName(string functor)
            {
                SharedGlobalPredDefsDirty = true;
                functor = ProperCase(functor);
                if (argNames.Add(functor) || !predicateArgName.ToLower().Contains(functor.ToLower()))
                {
                    predicateArgName = predicateArgName + functor;
                }
            }

            private static string ProperCase(string functor)
            {
                functor = functor.Substring(0, 1).ToUpper() + functor.Substring(1);
                return functor;
            }

            public bool NameMatches(string name)
            {
                return predicateArgName.Contains(ProperCase(name));
            }
        }

        public partial class PNode : IComparable
        {
            public string id;
            public object CompileLock
            {
                get
                {
                    var lockobj = this;
                    bool needsUnlock = true;
                    try
                    {
                        needsUnlock = Monitor.TryEnter(lockobj, TimeSpan.FromSeconds(4));
                        if (!needsUnlock)
                        {
                            return new object();
                        }
                        return lockobj;
                    }
                    finally
                    {
                        if (needsUnlock)
                        {
                            Monitor.Exit(lockobj);
                        }
                    }
                }
            }

            readonly public PDB pdb;

            public bool dirty
            {
                get
                {
                    checkSyncLocked();
                    lock (CompileLock)
                    {
                        return IsOutOfSyncFor(SourceKind);
                    }
                }
            }

            private void checkSyncLocked()
            {
                LockInfo.EnsureLocked(CompileLock, Warn);
            }

            public double probability = 1.0;
            public ContentBackingStore SourceKind = ContentBackingStore.Prolog;

            private ContentBackingStore _SyncFromNow = ContentBackingStore.None;
            public ContentBackingStore SyncFromNow
            {
                get { return _SyncFromNow; }
                set
                {
                    if (_SyncFromNow == value) return;
                    if (value != ContentBackingStore.None && _SyncFromNow != ContentBackingStore.None)
                    {
                        Warn("Might be losing Data: SyncFromNow {0}=>{1}", _SyncFromNow, value);
                    }
                    _SyncFromNow = value;
                }
            }

            List<PEdge> incomingEdgeList = new List<PEdge>();
            List<PEdge> outgoingEdgeList = new List<PEdge>();

            public string Id
            {
                get { return id; }
                set { id = value; }
            }
            object _repository;
#if MERGED_RDFSTORE
            public GraphWithDef RdfStore
            {
                get { return this; }
            }
#else
            public GraphWithDef RdfStore;
#endif


            public object Repository
            {
                get { return _repository; }
                set
                {
                    if (value == null)
                    {
                        SourceKind = ContentBackingStore.RdfMemory;
                    }
                    else
                    {
                        SourceKind = ContentBackingStore.RdfServerURI;
                    }
                    if (_repository == value) return;
                    SyncFromNow = SourceKind;
                    _repository = value;
                }
            }

            public double Probability
            {
                get { return probability; }
                set { probability = value; }
            }
            public override int GetHashCode()
            {
                return base.GetHashCode();
                return id.GetHashCode();
            }
            public override bool Equals(object obj)
            {
                PNode otherNode = obj as PNode;
                if (otherNode == null)
                    return false;

                return otherNode.id == this.id;
            }

            public static bool operator ==(PNode node1, PNode node2)
            {
                if (Object.ReferenceEquals(node1, node2))
                    return true;
                if (Object.ReferenceEquals(node1, null) || Object.ReferenceEquals(node2, null))
                    return false;

                return node1.Equals(node2);
            }

            public static bool operator !=(PNode node1, PNode node2)
            {
                return !(node1 == node2);
            }


            public override string ToString()
            {
                return "mt:" + Id + " " + DebugInfo;
            }

            public PEdge CreateEdgeTo(PNode otherNode)
            {
                if (otherNode == this)
                {
                    return null;
                }
                PEdge edge = new PEdge(this, otherNode);
                return edge;
            }

            public void AddIncomingEdge(PEdge edge)
            {
                lock (EdgeLists) if (!incomingEdgeList.Contains(edge)) incomingEdgeList.Add(edge);
            }

            public object EdgeLists
            {
                get { return GlobalEdgeListLock; }
            }

            public static object GlobalEdgeListLock = new object();

            public void AddOutgoingEdge(PEdge edge)
            {
                lock (EdgeLists) if (!outgoingEdgeList.Contains(edge)) outgoingEdgeList.Add(edge);
            }
            public void ClearIncomingEdges()
            {
                lock (EdgeLists) incomingEdgeList.Clear();
            }
            public void ClearOutgoingEdges()
            {
                lock (EdgeLists) outgoingEdgeList.Clear();
            }
            public PEdge[] IncomingEdges
            {
                get { lock (EdgeLists) return incomingEdgeList.ToArray(); }
            }

            public PEdge[] OutgoingEdges
            {
                get { lock (EdgeLists) return outgoingEdgeList.ToArray(); }
            }

            public string DebugInfo
            {
                get
                {
                    string prefix = string.Format("source={0}", SourceKind);
                    if (probability != 1.0)
                    {
                        prefix += string.Format(" prob={0}", probability);
                    }
                    string pq = "";
                    var buri = RdfStore.rdfGraph.BaseUri;
                    if (buri != null) pq = "" + buri;
                    return string.Format("{0} size={1} dirty={2} triples={3} sync={4} repo={5} base={6}",
                                         prefix, pdb.rules.Count, dirty,
                                         RdfStore.rdfGraph.Triples.Count,
                                         SyncFromNow, Repository, pq);
                }
            }

            public IGraph definations
            {
                get { return RdfStore.rdfGraph; }
            }

            public bool RdfCacheShouldGenlPrologMt = false;

            public bool EdgeAlreadyExists(PNode otherNode)
            {
                foreach (PEdge e in this.OutgoingEdges)
                {
                    if (e.EndNode == otherNode)
                    {
                        return true;
                    }
                }

                return false;
            }

            public void RemoveEdgeTo(PNode otherNode)
            {
                foreach (PEdge e in this.OutgoingEdges)
                {
                    if (e.EndNode == otherNode)
                    {
                        if (e.StartNode.outgoingEdgeList.Contains(e))
                            e.StartNode.outgoingEdgeList.Remove(e);
                        if (e.EndNode.incomingEdgeList.Contains(e))
                            e.EndNode.incomingEdgeList.Remove(e);
                        return;
                    }
                }

                return;
            }

            /// <summary>
            /// IComparable.CompareTo implementation.
            /// </summary>
            public int CompareTo(object obj)
            {
                if (obj is PNode)
                {
                    PNode temp = (PNode)obj;

                    return probability.CompareTo(temp.probability);
                }

                throw new ArgumentException("object is not a PNode");
            }


            internal string ToLink(string serverRoot)
            {
                return string.Format("<a href='{1}siprolog/?mt={0}'>{0}</a>&nbsp;({2})", id, serverRoot, DebugInfo.Replace(" ", "&nbsp;"));
            }

            public bool IsDataFrom(ContentBackingStore backingStore)
            {
                return SourceKind == backingStore;
            }

            internal bool ClearRDFCache()
            {
                if (RdfStore.rdfGraph.IsEmpty) return false;
                checkSyncLocked();
                RdfStore.rdfGraph.Clear();
                return true;
            }
            internal bool ClearPrologCache()
            {
                if (pdb.rules.Count == 0) return false;
                checkSyncLocked();
                pdb.index.Clear();
                lock (CompileLock) lock (pdb.rules) pdb.rules.Clear();
                return true;
            }

            internal void Clear()
            {
                lock (CompileLock)
                {
                    ClearPrologCache();
                    ClearRDFCache();
                    SyncFromNow = SourceKind;
                }
            }

            public void pushRdfGraphToPrologKB(bool clearPrologKB)
            {
                if (IsOutOfSyncFor(ContentBackingStore.RdfMemory))
                {
                    Warn("RdfMemory not ready for pushing " + this);
                }
                lock (CompileLock) RdfStore.pushGraphToKB(clearPrologKB);
            }
            public void pushPrologKBToRdfGraph(bool clearRDFMemory)
            {
                if (IsOutOfSyncFor(ContentBackingStore.Prolog))
                {
                    Warn("Prolog not ready for pushing " + this);
                }
                lock (CompileLock) RdfStore.pushRulesToGraph(clearRDFMemory);
            }

            public bool IsOutOfSyncFor(ContentBackingStore type)
            {
                return type != SyncFromNow && SyncFromNow != ContentBackingStore.None;
            }

            public void populateRDFMemoryFromRepository()
            {
                Uri from = Repository as Uri;
                if (from == null)
                {
                    string uri = "" + Repository;
                    from = UriFactory.Create(uri);
                }
                RdfStore.rdfGraph.BaseUri = from;
                RdfStore.LoadFromUri(from);
                SyncFromNow = ContentBackingStore.RdfMemory;
            }
#if MERGED_RDFSTORE
#else

            public PNode(string id)
                : this(id, null)
            {
            }
                        
            public PNode(string id, object info)
            {
                this.id = id;
                pdb = new PDB(true);
                pdb.startMt = id;
                pdb.followedGenlMt = false;
                this._repository = info;
            }
        }

        public partial class GraphWithDef {
            public IGraph definations
            {
                get { return rdfGraph ?? rdfDefinations ?? rdfGraph; }
            }

            protected GraphWithDef RdfStore
            {
                get { return this; }
            }

            /// <summary>
            /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </summary>
            /// <returns>
            /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </returns>
            /// <filterpriority>2</filterpriority>
            /// <summary>
            /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </summary>
            /// <returns>
            /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </returns>
            /// <filterpriority>2</filterpriority>
            public override string ToString()
            {
                return "PrologKB=" + PrologKB + StructToString(this);
            }
#endif
            public string AToString
            {
                get { return ToString(); }
            }

            readonly public IGraph _rdfGraph;
            public IGraph rdfGraph
            {
                get
                {
                    return _rdfGraph;
                }
            }

            public string prologMt;

            static public List<PredicateProperty> localPreds = new List<PredicateProperty>();
            static public List<RDFArgSpec> localArgTypes = new List<RDFArgSpec>();
            static public List<Term> localPredInstances = new List<Term>();
            private PNode kbNode;
            public SIProlog prologEngine;
            
#if MERGED_RDFSTORE
            public PNode PrologKB
            {
                get { return this; }
            }
            public PNode(string plMt, SIProlog prolog, IGraph data)
            {
                this.prologEngine = prolog;
                prologMt = plMt;
                this.id = plMt;
                pdb = new PDB(true);
                pdb.startMt = plMt;
                pdb.followedGenlMt = false;
                PrologKB.id = plMt;
                _rdfGraph = data;
                prolog.KBGraph.AddNode(this);
                prologEngine.GraphForMT[plMt] = this;
                EnsureGraphPrefixes(rdfGraph);
            }
#else
            public PNode PrologKB
            {
                get
                {
                    if (kbNode == null)
                        lock (prologEngine.KBGraph)
                        {
                            kbNode = prologEngine.KBGraph.Contains(prologMt);
                            if (kbNode != null) return kbNode;
                            kbNode = new PNode(prologMt) {RdfStore = this, id = prologMt};
                            prologEngine.KBGraph.AddNode(kbNode);
                        }
                    if (kbNode.RdfStore == null)
                    {
                        kbNode.RdfStore = this;
                    }
                    else if (kbNode.RdfStore != this)
                    {
                        kbNode.RdfStore = this;
                    }
                    return kbNode;
                }
                set
                {
                    kbNode = value;
                    kbNode.RdfStore = this;
                }
            }
            public GraphWithDef(string plMt, SIProlog prolog, IGraph data)
            {                
                this.prologEngine = prolog;
                prologMt = plMt;
                PrologKB.id = plMt;
                _rdfGraph = data;
                EnsureGraphPrefixes(rdfGraph);
            }
#endif

            static private PredicateProperty AddDefs(Rule rule)
            {
                PredicateProperty headPP = GetPredicateProperty(rule.head); ;
                if (rule.body != null)
                {
                    foreach (var p in rule.body.plist.ArgList)
                    {
                        if (!(p is Term)) continue;
                        GetPredicateProperty((Term)p);
                    }
                }
                return headPP;
            }
            public INode C(string p0)
            {
                return C(definations, p0);
            }

            static public PredicateProperty GetPredicateProperty(Term term)
            {
                DocumentTerm(term, true);
                return GetPredicateProperty(term.name, term.Arity);
            }
            static public PredicateProperty GetPredicateProperty(string predName0, int arity)
            {
                string predName = Unsymbolize(predName0);
                PredicateProperty def;
                bool newlyCreated;
                var rdef = rdfDefinations;
                lock (SharedGlobalPredDefs)
                {
                    def = GetPredDef(predName, arity, out newlyCreated);
                    if (newlyCreated)
                    {
                        SharedGlobalPredDefsDirty = true;
                        def.RdfDocumentPred(rdef);
                    }
                }
                lock (localPreds)
                {
                    if (!localPreds.Contains(def))
                    {
                        localPreds.Add(def);
                        SharedGlobalPredDefsDirty = true;
                    }
                }
                //if (newlyCreated)
                {
                    SharedGlobalPredDefsDirty = true;
                    foreach (Triple t in def.DefinitionalRDF)
                    {
                        rdfGraphAssert(rdef, t);
                    }
                }
                return def;
            }

            public static RDFArgSpec GetAdef(PredicateProperty def, int argNum1Based, bool okToMake)
            {
                RDFArgSpec adef;
                lock (def.argDefs)
                {
                    if (!def.argDefs.TryGetValue(argNum1Based, out adef))
                    {
                        if (!okToMake) return null;
                        adef = def.argDefs[argNum1Based] = new RDFArgSpec(argNum1Based);
                        adef.AddDomainType(def, argNum1Based + 1);
                    }
                }
                return adef;
            }

            static private string Unsymbolize(string name0)
            {
                if (name0 == "." || name0 == FUNCTOR_CONS) return "cons";
                if (name0 == "[]" || name0 == FUNCTOR_NIL) return "nil";
                string n = HttpUtility.UrlEncode(name0);
                return n;
            }
            static private string Symbolize(string name0)
            {
                if (name0 == "cons") return FUNCTOR_CONS;
                if (name0 == "nil") return FUNCTOR_NIL;
                string n = HttpUtility.UrlDecode(name0);
                return n;
            }

            public static PredicateProperty GetPredDef(string predName, int arity, out bool newlyCreated)
            {
                PredicateProperty def;
                string key = Unsymbolize(predName);// +"_" + arity;
                lock (SharedGlobalPredDefs)
                {
                    if (!SharedGlobalPredDefs.TryGetValue(key, out def))
                    {
                        newlyCreated = true;
                        string predClassName = "PredClass_" + key;
                        SharedGlobalPredDefs[key] =
                            def =
                            new PredicateProperty(arity) { name = predName, keyname = key, classname = predClassName };
                        return def;
                    }
                }
                newlyCreated = false;
                return def;
            }

            public static INode CExtracted(IGraph def, string p)
            {
                bool colm = p.Contains(":");
                if (!colm)
                {
                    bool decm = p.Contains(".");
                    long intv;
                    if (!decm && long.TryParse(p, out intv))
                    {
                        return new LongNode(def, intv);
                    }
                    double dbl;
                    if (decm && double.TryParse(p, out dbl))
                    {
                        return new DoubleNode(def, dbl);
                    }
                }
                return C(def, p);
            }

            static public INode C(IGraph def, string p0)
            {
                var p = p0.Trim();
                if (p.StartsWith("_:"))
                {
                    return def.CreateBlankNode(p.Substring(2));
                }
                if (p.StartsWith("?"))
                {
                    return def.CreateVariableNode(p.Substring(1));
                }
                int colm = p.IndexOf(":");
                bool uir = p.Contains("#") || colm == 0 || colm > 1;
                bool triedURINode = false;


                try
                {
                    Uri newUri;
                    if (colm == -1)
                    {
                        lock (GuessedNameSpace)
                        {
                            KeyValuePair<string, string> uriGuess;
                            if (GuessedNameSpace.TryGetValue(p, out uriGuess))
                            {
                                var pref = uriGuess.Value ?? (uriGuess.Key + ":");
                                return def.CreateUriNode(pref + "" + p);
                            }
                        }
                    }
                    else if (IsAbsoluteURI(p))
                    {
                        newUri = new Uri(p);
                        return def.CreateUriNode(newUri);
                    }
                    string qname = Tools.ResolveQName(p0, def.NamespaceMap, def.BaseUri);
                    return C(def, qname);
                }
                catch (Exception)
                {
                    triedURINode = true;
                }
                int badchars = p.IndexOfAny("? ".ToCharArray());
                if (badchars >= 0)
                {
                    return CreateLiteralNode(def, p);
                }
                if (!triedURINode)
                {
                    var node = CUrlNode(def, p, colm == -1, true);
                    if (node != null) return node;
                }
                badchars = p.IndexOfAny("? ,$.&!@#$%^&*()+".ToCharArray());
                if (badchars >= 0)
                {
                    string p2 = HttpUtility.UrlEncode(HttpUtility.UrlDecode(p));
                    if (p2 != p)
                    {
                        var node = CUrlNode(def,p2, colm == -1, false);
                        if (node != null) return node;
                    }
                }
                return CreateLiteralNode(def,p);
            }

            public static bool IsAbsoluteURI(string s)
            {
                int idx = s.IndexOf(":");
                return idx > 1 && s.Substring(idx).Contains("/") && Uri.IsWellFormedUriString(s, UriKind.Absolute);
            }

            private static INode CreateLiteralNode(INodeFactory def, string p)
            {
                var vnode = GetValuedNode(p);
                if (vnode != null) return vnode;
                return def.CreateLiteralNode(p);
            }

            static public IUriNode CUrlNode(IGraph definations, string p, bool checkSuffixes, bool allowRelative)
            {
                lock (GuessedNameSpace)
                {
                    KeyValuePair<string, string> uriGuess;
                    Uri newUri;
                    if (checkSuffixes && GuessedNameSpace.TryGetValue(p, out uriGuess))
                    {
                        var pref = uriGuess.Value ?? (uriGuess.Key + ":");
                        try
                        {
                            return definations.CreateUriNode(pref + "" + p);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    if (IsAbsoluteURI(p))
                    {
                        try
                        {
                            newUri = new Uri(p);
                            return definations.CreateUriNode(newUri);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    if (allowRelative && Uri.IsWellFormedUriString(p, UriKind.Relative))
                    {
                        try
                        {
                            newUri = new Uri(definations.BaseUri + p, UriKind.Absolute);
                            //var newUri2 = new Uri(definations.BaseUri, newUri);
                            var r = definations.CreateUriNode(newUri);
                            return r;
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
                return null;
            }

            static public bool TryCreateCleanUri(Uri baseUri, string relstr, out Uri result)
            {
                try
                {
                    if (!Uri.TryCreate(baseUri, relstr, out result))
                    {
                        return false;
                    }
                }
                catch (UriFormatException ex)
                {
                    throw new InvalidOperationException(
                        String.Format("Can create URI for base={0}, rel={1}", baseUri.ToString(), relstr), ex);
                }
                return true;
            }
            public static INode InstanceOf
            {
                get
                {
                    var ret = C(rdfDefinations, "rdf:type");
                    return ret;
                }
            }
            public static INode SameAs
            {
                get
                {
                    var ret = C(rdfDefinations, "owl:sameAs");
                    return ret;
                }
            }
            public static INode IsDefinedBy
            {
                get
                {
                    var ret = C(rdfDefinations, "rdfs:isDefinedBy");
                    return ret;
                }
            }
            public static INode PrologPredicate
            {
                get { return C(rdfDefinations, "rdf:Property"); }
            }
            public static INode PrologPredicateClass
            {
                get { return C(rdfDefinations, "rdf:Class"); }
            }

            public static RdfRules FromRule(Rule rule, IGraph kb)
            {
                if (rule.rdfRuleCache != null) return rule.rdfRuleCache;
                if (IsRdfPrecoded(rule.head))
                {
                    return null;
                }
              
                var rdfRules = rule.rdfRuleCache = new RdfRules(kb);
                PartList pl = null;
                if (rule.body != null)
                {
                    if (!IsBodyAlwaysTrue(rule.body.plist))
                    {
                        pl = (PartList)rule.body.plist.CopyTerm;
                    }
                }
                Term rulehead = (Term) rule.head.CopyTerm;
                AddData(rulehead, pl, rdfRules);
                if (rdfRules.RuleNode != null)
                {
                  //  EnsureGraphPrefixes(kb, () => UriOfMt(rule.optHomeMt));
                    rdfRules.AddProducing(MakeTriple(rdfRules.RuleNode,
                                                     kb.CreateUriNode("siprolog:sourceCode"),
                                                     kb.CreateLiteralNode(rule.ToSource(SourceLanguage.Prolog), "prolog")));
                }
                rdfRules.AssertTriples(kb, true, true);
                return rdfRules;
            }

            public void AddRuleToRDF(Rule rule)
            {
                if (rule.rdfRuleCache != null) return;
                string before = rule.ToSource(SourceLanguage.Prolog);
                FromRule(rule, rdfGraph);
                string after = rule.ToSource(SourceLanguage.Prolog);
                if (before != after)
                {
                  //  Warn("Manipulated rule: " + before + "->" + after);
                }
            }

            static bool IsRdfPrecoded(Term thisTerm)
            {
                var name = thisTerm.name;
                string key = name + "_" + thisTerm.Arity;
                if (key == "not_1")
                {
                    return true;
                }
                if (key == "unify_2")
                {
                    return true;
                }
                if (key.Contains("sameAs"))
                {
                    return true;
                }
                if (key.Contains(":"))
                {
                    return true;
                }
                if (QueryPredicateInfo(name, "prologMappingType") == "PrologOnlyPredicate")
                {
                    return true;
                }
                return false;
            }
            static bool IsRdfBuiltIn(string name, int arity, Part thisTerm, RdfRules rules)
            {

                if (arity < 2)
                {
                    if (arity == 1 && IsLitteral(thisTerm.ArgList[0], rules))
                    {
                        return false;
                    }
                    return true;
                }
                if (arity > 2)
                {
                    if (name == TripleName) return true;
                    return false;
                }
                if (arity == 2 && name.StartsWith("prolog")) return true;
                if (arity == 2 && name.Contains(":")) return true;
                lock (PDB.builtin)
                {
                    if (PDB.builtin.ContainsKey(name + "/" + arity))
                    {
                        return true;
                    }
                }

                if (QueryPredicateInfo(name, "prologMappingType") == "DirectOneToOneMapping")
                {
                    return true;
                }
                if (QueryPredicateInfo(name, "prologMappingTypeEquivalent") != null)
                {
                    return true;
                }
                string type = QueryPredicateInfo(name, "rdf:type");
                if (type != null)
                {
                    if (type.EndsWith("Property"))
                    {
                        return true;
                    }
                }
                return false;
            }

            public static string QueryPredicateInfo(string thisTerm, string pred)
            {
                SparqlParameterizedString query = new SparqlParameterizedString("SELECT ?object WHERE { @subj @pred ?object }");
                query.SetParameter("subj", C(rdfDefinations, thisTerm));
                query.SetParameter("pred", C(rdfDefinations, pred));
                object result = rdfDefinations.ExecuteQuery(query);
                if (result == null)
                {
                    return null;
                }
                SparqlResultSet rs = result as SparqlResultSet;
                string answer = null;
                if (rs.Count > 0)
                {
                    var res = rs[0];
                    var r = res["object"];
                    answer = GetTextString(r);
                }
                return answer;
            }

            private static string GetTextString(INode r)
            {               
                var sr = "" + r;
                var vn = ToValueNode(r);
                if (vn != r) r = vn;
                if (r is IUriNode)
                {
                    IUriNode uriNode = r as IUriNode;
                    sr = uriNode.Uri.Fragment;
                }
                else if (r is ILiteralNode)
                {
                    sr = ((ILiteralNode)r).Value;
                }
                if (sr.StartsWith("#")) sr = sr.Substring(1);
                return sr;
            }

            private static bool IsLitteral(Part arg0, RdfRules triples)
            {
                if (arg0 == null) return true;
                var partToRdf = PartToRdf(arg0, triples);
                return partToRdf is ILiteralNode;
            }

            static private void AddData(Term head, PartList rulebody, RdfRules triples)
            {
                var varNames = new List<string>();
                var newVarNames = new List<string>();
                int newVarCount;
               /// head = ToTranslated(head, triples);
                lock (head)
                {
                    rulebody = AnalyzeHead(head, true, varNames, newVarNames, out newVarCount, rulebody);
                    AddData(triples, head, rulebody, varNames, newVarCount, newVarNames);
                }
            }

            public static PartList AnalyzeHead(Part head, bool replaceVars, ICollection<string> varNames, ICollection<string> newVarNames, out int newVarsNeeded, PartList rulebody)
            {
                int newVarCount = 0;
                PartList[] pl = {null};
                head.Visit((a, pr) =>
                               {
                                   if (!(a is Variable))
                                   {
                                       a.Visit(pr);
                                       return a;
                                   }
                                   string an = a.name;
                                   if (newVarNames != null && newVarNames.Contains(an))
                                   {
                                       // Dont copy previously copied vars
                                       return a;
                                   }
                                   if (varNames != null && !varNames.Contains(an))
                                   {
                                       // First time found
                                       varNames.Add(an);
                                       return a;
                                   }
                                   if (!replaceVars)
                                   {
                                       newVarCount++;
                                       return a;
                                   }
                                   // copy the var to: newVarName
                                   string newVarName = an + newVarCount;
                                   if (newVarNames != null) newVarNames.Add(newVarName);
                                   var r = new Variable(newVarName);
                                   // add the unification to the partlist
                                   var lpl = pl[0] = pl[0] ?? new PartList();
                                   newVarCount++;
                                   lpl.AddPart(unifyvar(a, r));
                                   return r;
                               });
                newVarsNeeded = newVarCount;
                PartListImpl bpl = pl[0];
                if (bpl == null)
                {
                    return rulebody;
                }
                if (newVarCount > 0)
                {
                    if (rulebody != null)
                    {
                        foreach (Part p in rulebody)
                        {
                            bpl.AddPart(p);
                        }
                    }
                }
                return bpl;
            }

            static private void AddData(RdfRules rdfRules, Term head, PartList rulebody, List<string> varNames, int newVarCount, List<string> newVarNamesMaybe)
            {
                if (IsRdfPrecoded(head))
                {
                    return;
                }
                lock (head)
                {
                    int newVarCount2;
                    var newVarNames = varNames;
                    varNames = new List<string>();
                    PartList bpl = AnalyzeHead(head, true, varNames, newVarNames, out newVarCount2, rulebody);
                    if (newVarCount2 > 0)
                    {
                        if (rulebody != null)
                        {
                            foreach (Part p in rulebody)
                            {
                                bpl.AddPart(p);
                            }
                        }
                    }
                    if (bpl == null)
                    {
                        if (rulebody != null)
                        {
                            bpl = rulebody;
                        }
                    }
                    rulebody = bpl;
                }
                AddData2(head, rulebody, rdfRules);
            }

            private static void AddData2(Term head, PartListImpl rulebodyIn, RdfRules rdfRules)
            {
                PartList rulebody = null;
                if (!IsBodyAlwaysTrue(rulebodyIn))
                {
                    rulebody = new PartListImpl();
                    foreach(Term tin in rulebodyIn)
                    {
                        Term t = ToTranslated(tin, rdfRules);
                        if (t != null) rulebody.AddPart(t);
                    }
                }
                if (head.name == "rdfinfo")
                {
                    var pl = SIProlog.CurrentProlog;
                    foreach (var arg in head.ArgList)
                    {

                        pl.appendKB(new RuleList() {new Rule((Term) arg)}, pl.FindOrCreateKB(rdfDefMT));
                    }
                    return;
                }
                var ruleSubject = CreateConsequentNode(head, rdfRules, rulebody != null);
                if (ruleSubject != null)
                {
                    rdfRules.RuleNode = ruleSubject;
                }
                if (rulebody != null)
                {
                    foreach (Part p in rulebody.ArgList)
                    {
                        GatherTermAntecedants(p, rdfRules);
                    }
                }
                var definations = rdfRules.def;
                string bad = rdfRules.Check(definations);
                if (!string.IsNullOrEmpty(bad))
                {
                    bad += " in DB " + rdfRules.ToString();
                    Warn(bad);
                }
                rdfRules.RequirementsMet = string.IsNullOrEmpty(rdfRules.Check(definations));
            }

            static private void GatherTermAntecedants(Part part, RdfRules anteceeds)
            {
                if (part is Term)
                {
                    var rdf = CreateAntecedantNode((Term) part, anteceeds);
                    if (rdf != null)
                    {
                        anteceeds.AddSubject(rdf);
                    }
                    return;
                }
                throw ErrorBadOp("Part is not a Term " + part);
            }

            private static bool IsTriple(Term term)
            {
                if( term.name == TripleName) return true;
                return false;
            }

            static private Term unifyvar(Part p1, Variable p2)
            {
                return MakeTerm("unify", p1, p2);
            }

            public static Triple CreateImplication(IGraph def, ICollection<Triple> bodytriples, ICollection<Triple> headtriples)
            {
                return MakeTriple(ToBracket(def, bodytriples), def.CreateUriNode("log:implies"),
                                  ToBracket(def, headtriples));
            }

            public static IGraphLiteralNode ToBracket(INodeFactory def, ICollection<Triple> bodytriples)
            {
                Graph subgraph = new Graph();
                foreach (Triple triple in bodytriples)
                {
                    subgraph.Assert(triple);
                }
                var group = def.CreateGraphLiteralNode(subgraph);
                return group;
            }

            static private INode CreateTriplesWithGeneratedSubject(Term term, RdfRules triples, bool isPrecond)
            {
                if (IsRdfBuiltIn(term.name, term.Arity, term, triples))
                {
                    Warn("RDFBuiltin passed to Create Subject");
                    INode bio;
                    if (BuiltinToRDF(!isPrecond, term, triples, isPrecond ? (Action<Triple>)triples.AddRequirement : triples.AddConsequent, out bio))
                        return bio;
                }
                var headDef = GetPredicateProperty(term);
                INode subj = CreateInstance(headDef, term, triples, isPrecond ? NodeType.Variable : NodeType.Uri);
                triples.AddSubject(subj);
                var conds = AddTriplesSubject(term, triples, subj);
                foreach (Triple triple in conds)
                {
                    if (isPrecond)
                    {
                        triples.AddRequirement(triple);
                    }
                    else
                    {
                        triples.AddConsequent(triple);
                    }
                }
                if (subj.NodeType != NodeType.Blank) return null;
                return subj;
            }

            static private List<Triple> AddTriplesSubject(Term term, RdfRules triples, INode subj)
            {
                int argNum = 1;
                var conds = new List<Triple>();
                var headDef = GetPredicateProperty(term);
                foreach (Part part in term.ArgList)
                {
                    RDFArgSpec argDef = GetAdef(headDef, argNum, false);
                    if (part is Variable)
                    {
                        if (argDef == null)
                        {
                            argDef = GetAdef(headDef, argNum, true);
                            argDef.AddRangeTypeName(part.name);
                        }
                    }
                    INode obj = PartToRdf(part, triples);
                    if (argDef == null)
                    {
                        argDef = GetAdef(headDef, argNum, true);
                    }
                    INode pred = argDef.GetRefNode(rdfDefinations);
                    conds.Add(MakeTriple(subj, pred, obj, false));
                    argNum++;
                }
                return conds;
            }

            static private INode CreateConsequentNode(Term term, RdfRules triples, bool hasBody)
            {
                term = ToTranslated(term, triples);
                if (IsRdfBuiltIn(term.name, term.Arity, term, triples))
                {
                    INode bio;
                    if (BuiltinToRDF(!hasBody, term, triples, triples.AddConsequent, out bio)) return null;
                }
                var rdf0 = CreateTriplesWithGeneratedSubject(term, triples, false);
                triples.AddSubject(rdf0);
                return rdf0;
            }

            static private INode CreateAntecedantNode(Term term, RdfRules triples)
            {
                term = ToTranslated(term, triples);
                if (IsRdfBuiltIn(term.name, term.Arity, term, triples))
                {
                    INode bio;
                    if (BuiltinToRDF(false, term, triples, triples.AddRequirement, out bio)) return bio;
                }
                var rdf0 = CreateTriplesWithGeneratedSubject(term, triples, true);
                triples.AddSubject(rdf0);
                return rdf0;
            }

            private static Term ToTranslated(Term term, RdfRules triples)
            {
                if (term.Arity == 0) return ToTranslated(MakeTerm("asserted", Atom.FromSource(term.name)), triples);
                if (term.Arity == 1)
                {
                    Part arg0 = term.ArgList[0];
                    if (false && !IsLitteral(arg0, triples))
                        return ToTranslated(
                            MakeTerm("rdf:type", arg0, Atom.FromSource(PredicateToType(term.name))), triples);
                    return ToTranslated(
                        MakeTerm("prologUnaryTrue", arg0, Atom.FromSource(PredicateToType(term.name))), triples);
                }
                if (term.Arity == 2) return term;
                if (term.Arity > 2)
                {
                    // TO(DO maybe translate here
                    //var satementTerm = new Variable("TERM" + CONSP);
                    return term;
                }
                return term;
            }

            static private bool BuiltinToRDF(bool toplevel, Term term, RdfRules antecedants, Action<Triple> howToAdd, out INode bio)
            {
                var definations = antecedants.def;
                int arity = term.Arity;
                if (arity == 2)
                {
                    bio = PartToRdf(term.ArgList[0], antecedants);
                    howToAdd(MakeTriple(bio,
                                        PredicateToProperty(term.name),
                                        PartToRdf(term.ArgList[1], antecedants), toplevel));
                    bio = null;
                    return true;
                }
                if (term.name == TripleName && arity == 3)
                {
                    var al = term.ArgList;
                    bio = PartToRdf(al[0], antecedants);
                    howToAdd(MakeTriple(bio, PartToRdf(al[1], antecedants),
                               PartToRdf(al[2], antecedants)));
                    bio = null;
                    return true;
                }
                if (arity == 1)
                {
                    bio = PartToRdf(term.ArgList[0], antecedants);
                    if (!(bio is ILiteralNode))
                    {
                        var dataType = C(definations, PredicateToType(term.name));
                        howToAdd(MakeTriple(bio, InstanceOf, dataType));
                        return true;
                    }
                }          
                ErrorBadOp("Cannot create RDF (Builtin) from " + term);
                bio = null;
                return false;                
            }

            static string PredicateToType(string unaryPred)
            {
                if (unaryPred == "call")
                {
                    return "rdf:Statement";
                }
                if (unaryPred == "not")
                {
                    return "rdf:FalseStatement";
                }
                return AsURIString(unaryPred);
            }

            public static string AsURIString(string unaryPred)
            {
                if (unaryPred.Contains(":")) return unaryPred;
                return ":" + unaryPred;
            }

            static public INode PredicateToProperty(string binaryPred)
            {
                if (binaryPred == "unify")
                {
                    return C(rdfDefinations, "owl:sameAs");
                }
                return C(rdfDefinations, AsURIString(binaryPred));
            }
            static private INode CreateInstance(PredicateProperty headDef, Term term, RdfRules graph, NodeType nodeType)
            {
                int instanceOnArg = 0;
                var name = term.name;
                if (QueryPredicateInfo(name, "prologMappingType") == "DirectOneToOneMapping")
                {
                    instanceOnArg = 1;
                }
                string iarg = QueryPredicateInfo(name, "prologInstanceArg");
                if (iarg != null)
                {
                    int arg;
                    if (int.TryParse(iarg, out arg))
                    {
                        instanceOnArg = arg;
                    }
                }
                if (instanceOnArg > 0)
                {
                    INode inst = PartToRdf(term.ArgList[instanceOnArg - 1], graph);
                    if (!CanBeSubjectNode(inst))
                    {
                        Warn("Incompatible Subject node " + inst + " in " + term);
                    }
                    return inst;
                }
                return CreateInstance(headDef, graph, nodeType);
            }

            public static bool CanBeSubjectNode(INode node)
            {
                if (node==null)
                    return false;
                if (node.NodeType == NodeType.Blank) return true;
                if (node.NodeType == NodeType.Uri) return true;
                return false;
            }

            static private INode CreateInstance(PredicateProperty headDef, RdfRules graph, NodeType nodeType)
            {
                var definations = graph.def;
                int nxt = headDef.instanceNumber++;
                string iname = "PINST" + nxt + "_" + headDef.keyname;
                INode iln = null;
                switch (nodeType)
                {
                    case NodeType.Blank:
                        iln = definations.CreateBlankNode(iname);
                        break;
                    case NodeType.Variable:
                        iname = iname.Replace("_", "").Replace("_", "").ToUpper();
                        iln = definations.CreateVariableNode(iname); 
                        break;
                    case NodeType.Uri:
                        iln = C(definations, iname);
                        break;
                    case NodeType.Literal:
                        iln =  definations.CreateLiteralNode(iname);
                        break;
                    case NodeType.GraphLiteral:
                        throw new ArgumentOutOfRangeException("nodeType");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("nodeType");
                }
                var a = InstanceOf;
                var cn = headDef.classNode = headDef.classNode ?? C(rdfDefinations, RoboKindURI + headDef.classname);
                graph.AddProducing(MakeTriple(iln, a, cn, false));
                return iln;
            }

            static public INode PartToRdf(Part part, RdfRules triples)
            {
                var definations = triples.def;
                if (part is Atom)
                {
                    Atom atom = ((Atom)part);
                    var rdf = atom.AsRDFNode();
                    if (rdf != null)
                    {
                        return rdf;
                    }
                    Warn("Atom.AsValuedNode returned NULL" + part);
                }
                if (part is Variable)
                {
                    return definations.CreateVariableNode(part.name);
                }
                Part car, cdr;
                if (GetCons(part, out car, out cdr))
                {
                    var rdf = definations.CreateVariableNode("CONS" + CONSP);
                    triples.AddRequirement(rdf, "rdf:first", car);
                    triples.AddRequirement(rdf, "rdf:rest", cdr);
                    return rdf;
                }
                if (part is Term)
                {
                    return CreateAntecedantNode((Term)part, triples);
                }
                throw ErrorBadOp("ToRDF on " + part);
            }

            static readonly Dictionary<string, KeyValuePair<string, string>> GuessedNameSpace = new Dictionary<string, KeyValuePair<string, string>>();
            static public Part RdfToPart(INode node, RdfRules triples)
            {
                if (node is IVariableNode)
                {
                    var vnode = (IVariableNode) node;
                    return new Variable(vnode.VariableName);
                }
                if (node is IGraphLiteralNode)
                {
                    var vnode = (IGraphLiteralNode) node;
                    throw ErrorBadOp("RDFToPart: on " + vnode);
                }
                if (node is IBlankNode)
                {
                    var vnode = (IBlankNode) node;
                    var atom = Atom.MakeNodeAtom(vnode);
                    return atom;
                    node = triples.def.CreateUriNode(UriFactory.Create("_:" + vnode.InternalID));
                }

                if (node is IUriNode)
                {
                    var vnode = (IUriNode) node;
                    var atom = MakeUri(vnode.Uri.AbsoluteUri, triples, vnode);
                    return atom;
                }

                // all the below are now Literal Nodes of some type  (we divide into  "strings", numbers and "strings with"^"meaning" and 
                ILiteralNode litnode = node as ILiteralNode;
                if (litnode == null)
                {
                    throw ErrorBadOp("Cant find the nodetype on  " + node);
                }
                return Atom.MakeNodeAtom(litnode);
            }

            private static Part TriplesToProlog(IGraphLiteralNode vnode, RdfRules triples)
            {
                throw new NotImplementedException();
            }

            public static Atom MakeUri(string s, RdfRules triples, IUriNode node)
            {
                if (node != null && s == null)
                {
                    s = node.Uri.OriginalString;
                    var relitive = node.Uri.IsAbsoluteUri;
                    var uri2 = node.Uri.AbsoluteUri;
                    var quri = node.Uri.Query;
                }

                var tl_language = SourceLanguage.Notation3;
                do
                {
                    if (node != null)
                    {
                        var s2 = node.ToString();
                        var uri2 = node.Uri.AbsoluteUri;
                        if (s2 == uri2 && s == s2)
                        {
                            return Atom.MakeNodeAtom(node);
                        }
                    }

                    var definations = triples.def;
                    string prefix, uri, atom;
                    if (DevolveURI(definations.NamespaceMap, s, out uri, out prefix, out atom) || atom != null)
                    {
                        
                        Atom atomMake = Atom.MakeNodeAtom(node);
                        lock (GuessedNameSpace)
                        {
                            KeyValuePair<string, string> gns;
                            bool fnd = GuessedNameSpace.TryGetValue(atom, out gns);
                            GuessedNameSpace[atom] = new KeyValuePair<string, string>(prefix ?? gns.Key,
                                                                                      uri ?? gns.Value);
                        }
                        return atomMake;
                    }
                } while (false);
                do
                {
                    string quoting = MustGuessQuotes;
                    int hash = s.IndexOf("#");
                    s = s.Substring(1 + hash);
                    if (hash == -1 && !s.Contains(":/"))
                    {
                        hash = s.IndexOf(":");
                        if (hash > 0)
                        {
                            s = s.Substring(1 + hash);
                            quoting = SYNTAX_AtomQuotes;
                        }
                    }
                    if (hash == -1)
                    {
                        quoting = SYNTAX_AtomQuotes;
                    }
                    return Atom.MakeNodeAtom(Atom.MakeNode(s, quoting));
                } while (false);
            }

            private static HashSet<string> MissingNameSpaces = new HashSet<string>();
            public FrequencyOfSync SyncFrequency = FrequencyOfSync.AsNeeded;

            static private void DiscoverNameSpace(string uri)
            {
                if (MissingNameSpaces.Add(uri))
                {
                    Warn("New namespace that was missing: " + uri);
                }
            }
            static public bool DevolveURI(INamespaceMapper mapper, string s, out string uri, out string prefix, out string atom)
            {
                if (IsAbsoluteURI(s))
                {
                    string tries = HttpUtility.UrlDecode(s);
                    if (tries != s)
                    {
                        s = tries;
                    }
                }
                bool ret = DevolveURI0(mapper, s, out uri, out prefix, out atom);
                if (atom == "")
                {
                    string idea = prefix + ":" + atom;
                    if (idea.Length == 1)
                    {
                        atom = uri;
                    }
                    else
                    {
                        if (uri.Length > 0)
                        {
                            atom = uri;
                        }
                        else
                        {
                            atom = idea;
                        }
                    }
                }
                if (atom.Length == 0 || (!char.IsLetterOrDigit(atom[0]) && !atom.StartsWith("#C_") && !atom.StartsWith("$")))
                {
                //    Warn("strange atom='{0}' prefix='{1}' uri='{2}' ", atom, prefix, uri);
                    //string satom = HttpUtility.UrlEncode(atom);
                }
                if (atom == uri) return ret;
                if (prefix == null && uri != null)
                {
                    atom = s;
                    uri = s;
                    return ret;

                    if (!uri.Contains("/ns/") && !uri.Contains("robokind") && atom != uri)
                    {
                        DiscoverNameSpace(uri);
                    }
                    else
                    {
                        atom = uri;
                    }
                }
                return ret;
            }

            static public bool DevolveURI0(INamespaceMapper mapper, string s, out string uri, out string prefix, out string atom)
            {
                atom = null;
                uri = null;
                prefix = null;
                foreach (var pfx in mapper.Prefixes)
                {
                    prefix = pfx;
                    var uril = mapper.GetNamespaceUri(prefix);
                    uri = uril.ToString();
                    if (uri.Length > 0 && s.StartsWith(uri))
                    {
                        if (string.IsNullOrEmpty(prefix))
                        {
                            prefix = mapper.GetPrefix(uril);
                        }
                        atom = s.Substring(uri.Length);
                        return true;
                    }
                    string prefixc = prefix + ":";
                    if (s.StartsWith(prefixc))
                    {
                        atom = s.Substring(prefixc.Length);
                        return true;
                    }
                }
                atom = null;
                uri = null;
                prefix = null;

                int hash = s.LastIndexOf("#");
                if (hash > 0)
                {
                    uri = s.Substring(0, hash + 1);
                    prefix = null;
                    atom = s.Substring(hash + 1);
                    return true;
                }
                int col2 = s.IndexOf(":/");
                int slash = s.LastIndexOf("/");
                if (col2 > 0)
                {
                    if (slash != col2 + 1)
                    {
                        uri = s.Substring(0, slash + 1);
                        prefix = null;
                        atom = s.Substring(slash + 1);
                        return true;
                    }
                }
                int space = s.LastIndexOf(" ");
                int col = s.IndexOf(":");
                int coll = s.LastIndexOf(":");
                if (slash == -1 && col >= 0)
                {
                    if (space > 0) return false;
                    if (coll != col) return false;
                    uri = null;
                    prefix = s.Substring(0, col);
                    atom = s.Substring(col + 1);
                    return true;
                }

                return false;
            }

            internal void pushRulesToGraph(bool clearRDFMemory)
            {
                var focus = PrologKB;
                WarnAndClear(focus, ContentBackingStore.Prolog, clearRDFMemory, focus.ClearRDFCache, ContentBackingStore.RdfMemory);
                prologEngine.pushRulesToGraph(prologMt, this, focus.RdfCacheShouldGenlPrologMt);
                SaveOffRDF();
            }

            private void SaveOffRDF()
            {
                string rdfdir = "rdfcache";
                if (Directory.Exists(rdfdir))
                {
                    lock (CompileLock)
                    {
                        rdfGraph.SaveToFile(rdfdir + "/" + prologMt + ".n3", new Notation3Writer());
                    }
                }
            }

            internal void pushGraphToKB(bool clearPrologFirst)
            {
                SaveOffRDF();
                var focus = PrologKB;
                WarnAndClear(focus, ContentBackingStore.RdfMemory, clearPrologFirst, focus.ClearPrologCache,
                             ContentBackingStore.Prolog);
                var trips = Triples;
                if (trips.Count == 0) return;
                lock (trips)
                {
                    focus.pdb.index.Clear();
                    foreach (Triple triple in trips)
                    {
                        RdfRules rules = new RdfRules(rdfGraph);
                        var term = MakeTerm(TripleName,
                                            RdfToPart(triple.Subject, rules),
                                            RdfToPart(triple.Predicate, rules),
                                            RdfToPart(triple.Object, rules));
                        var rule = new Rule(term);
                        rule.rdfRuleCache = rules;
                        if (!focus.pdb.rules.Contains(rule))
                        {
                            focus.pdb.rules.Add(rule);
                        }
                    }
                }
            }

            private void WarnAndClear(PNode focus, ContentBackingStore fromContent, bool clearFirst, Func<bool> clearMethod, ContentBackingStore destContent)
            {
                if (focus.IsOutOfSyncFor(fromContent))
                {
                    Warn("IsOutOfSyncFor " + fromContent + " due to SyncFromNow==" + focus.SyncFromNow + " in " + focus);
                }
                if (clearFirst)
                {
                    if (clearMethod())
                    {
                        if (focus.SyncFromNow == destContent)
                        {
                            Warn("Lost Data from " + focus.SyncFromNow + " in " + this);
                        }
                    }
                    focus.SyncFromNow = ContentBackingStore.None;
                }
                else
                {
                    focus.SyncFromNow = ContentBackingStore.None;
                    focus.SyncFromNow = destContent;
                }
            }

            public BaseTripleCollection Triples
            {
                get { return rdfGraph.Triples; }
            }

            public string PlReadble(INode subject0, RdfRules rules)
            {
                var subject1 = ToValueNode(subject0);
                Part part1 = RdfToPart(subject1, rules);
                INode subject2 = PartToRdf(part1, rules);
                if (subject2 != subject1)
                {
                    Warn("PlReadble not round tripping! INodes " + NodeDesc(subject1) + "->" + NodeDesc(subject2));
                }
                string readable = part1.ToSource(SourceLanguage.Prolog);
                if (SIProlog.DeveloperSanityChecks < 2) return readable;
                Tokeniser oldTokenizer = new Tokeniser(readable);
                Part part2 = ParsePart(oldTokenizer);
                if (part2 == null || !part2.Equals(part1))
                {
                    string readable2 = null;
                    if (part2 != null) readable2 = part2.ToSource(SourceLanguage.Prolog);
                    tl_spy_prolog_reader = true;
                    Tokeniser newTokeniser = new Tokeniser(readable);
                    part2 = ParsePart(newTokeniser);
                    tl_spy_prolog_reader = false;
                    Warn("PlReadble not round tripping! re-readablity Node=" + NodeDesc(subject1) + " part1=" +
                         PartDesc(part1) + ".ToPLReadable()->" + readable);
                }
                if ((part2 is Variable && subject1.NodeType != NodeType.Variable))
                {
                    Warn("PlReadble not round tripping! Making a prolog variable? " + NodeDesc(subject1) + " part1= " + PartDesc(part1));
                }
                return readable;
            }

            static string NodeDesc(INode subject)
            {
                return subject + "{type=" + subject.NodeType + " impl=" + subject.GetType() + "}";
            }

            static string PartDesc(Part subject)
            {
                return subject + "{type=" + subject.type + " impl=" + subject.GetType() + "}";
            }

            public void LoadFromUri(Uri uri)
            {
                try
                {
                    LoadFromUri0(uri);
                }
                catch (Exception e)
                {
                    Warn("LoadFromURI({0}) Caused: {1}", uri, e);
                }
            }

            private void LoadFromUri0(Uri endpointURI)
            {
                Graph g = new Graph();
                g.NamespaceMap.Import(rdfGraph.NamespaceMap);
                g.LoadFromUri(endpointURI);
                var gt = g.Triples;
                if (gt.Count > 0)
                {
                    rdfGraph.NamespaceMap.Import(g.NamespaceMap);
                    rdfGraph.Assert(gt);
                    if (gt.SubjectNodes.Count() > 1)
                    {
                        // we have actual data
                        return;
                    }
                }
                //Define a remote endpoint
                //Use the DBPedia SPARQL endpoint with the default Graph set to DBPedia
                SparqlRemoteEndpoint endpoint = new SparqlRemoteEndpoint(endpointURI);

                //Use the extension method ExecuteQuery() to make the query against the Graph
                try
                {
                    //Object results = g.ExecuteQuery(query);
                    //Make a SELECT query against the Endpoint
                    SparqlResultSet results = endpoint.QueryWithResultSet("SELECT ?s ?p ?o WHERE { ?s ?p ?o } LIMIT 1400");
                    foreach (SparqlResult set in results)
                    {
                        rdfGraph.Assert(MakeTriple(set["s"], set["p"], set["o"]));
                    }
                }
                catch (RdfQueryException queryEx)
                {
                    //There was an error executing the query so handle it here
                    Warn("While endpointURI={0}\n\n{1}", endpointURI, queryEx);
                }
            }

            public void IncludeRDFUri(string filename)
            {
                Graph g = new Graph();
                g.NamespaceMap.Import(rdfGraph.NamespaceMap);
                g.BaseUri = rdfGraph.BaseUri;
                Uri loadFrom = new Uri(new Uri(filename).AbsoluteUri);
                g.LoadFromUri(loadFrom);
                ConsoleWriteLine("Loading " + g.Triples.Count + " from " + loadFrom);
                rdfGraph.Merge(g, true);
                ConsoleWriteLine("Merged " + rdfGraph.Triples.Count + " to " + rdfGraph);
            }
        }
        public static void Warn(string format, params object[] args)
        {
            DLRConsole.DebugLevel = 6;
            string write = DLRConsole.SafeFormat(format, args);
            TextWriter WarnWriter = WebLinksWriter.WarnWriter;
            if (WarnWriter != null)
            {
                try
                {
                    WarnWriter.WriteLine("<hr/><pre><font color=\"red\">{0}</font></pre><hr/>", write);
                    WarnWriter.Flush();
                    return;
                }
                catch (Exception)
                {
                }
            }
            DLRConsole.DebugWriteLine("{0}", write);
        }
        public static void Warn(object arg0)
        {
            Warn("{0}", arg0);
        }
        public static void Warn(string arg0)
        {
            Warn("{0}", arg0);
        }
        public static void ConsoleWriteLine(string format, params object[] args)
        {
            if (DLRConsole.DebugLevel < 1) DLRConsole.DebugLevel = 6;
            string write = DLRConsole.SafeFormat(format, args);
            DLRConsole.DebugWriteLine("{0}",write);
        }

        private static void WriteGraph(TextWriter writer, IGraph graph, IGraph defs, string named)
        {
            WriteGraph(writer, graph, "Data for " + named, false, true);
            if (graph != defs) WriteGraph(writer, defs, "Defs for " + named, true, true);
        }
        internal static void WriteGraph(TextWriter writer, IGraph graph, string named, bool plain, bool inHtml)
        {
            string printerName = "plain";
            if (plain)
            {
                DumpTriples(graph, writer, named);
                return;
            }
            else
            {
                IRdfWriter n3w;
                StringWriter dtt = new StringWriter();
                string dttToString = "error";
                bool prettyWillWorked = false;

                if (prettyWillWorked)
                {
                    try
                    {
                        n3w = new CompressingTurtleWriter()
                                  {DefaultNamespaces = graph.NamespaceMap, PrettyPrintMode = true};
                        n3w.Save(graph, dtt);
                        dttToString = dtt.ToString();
                        prettyWillWorked = true;
                        printerName = n3w.GetType().Name;
                    }
                    catch (Exception e)
                    {
                        dttToString = dtt.ToString();
                        prettyWillWorked = false;
                    }
                }
                if (!prettyWillWorked)
                {
                    n3w = new Notation3Writer() {DefaultNamespaces = graph.NamespaceMap, PrettyPrintMode = true};
                    dtt = new StringWriter();
                    try
                    {
                        lock (graph) n3w.Save(graph, dtt);
                        dttToString = dtt.ToString();
                        printerName = n3w.GetType().Name;
                    }
                    catch (Exception e)
                    {
                        if (inHtml)
                        {
                            writer.WriteLine("<pre><font color='red'>{0} {1} {2}</font></pre>", e.GetType(), e.Message,
                                             e.StackTrace);
                            printerName = "triples";
                            DumpTriples(graph, writer, named);
                        }
                        else
                        {
                            printerName = "plain";
                            DumpTriplesPlain(graph.Triples, writer, "{0}", graph);
                        }
                        return;
                    }
                }
                if (inHtml)
                {
                    writer.WriteLine("<h3>{0} KB {1}</h3>", named, printerName);
                    writer.WriteLine("<pre>");
                }
                dttToString = dttToString.Replace("\r\n", "\n").Replace("\r", "\n");
                foreach (string line in dttToString.Split('\n'))
                {
                    if (string.IsNullOrEmpty(line))
                    {
                        writer.WriteLine();
                        continue;
                    }
                    if (line.StartsWith("@prefix ")) continue;
                    var hline = line;
                    hline = (" " + hline).Replace(" robokind:", " rk:");
                    hline = hline.Replace("<robokind:", "<rk:");
                    if (inHtml) hline = hline.Replace("<", "&lt;").Replace(">", "&gt;");
                    writer.WriteLine(hline);
                }
                if (inHtml)
                {
                    writer.WriteLine("</pre>");
                }
            }
        }

        private static void DumpTriples(IGraph graph, TextWriter writer, string named)
        {
            var trips = graph.Triples;
            writer.WriteLine("<h3>{0} KB Triples {1}</h3>", named, trips.Count);
            writer.WriteLine("<pre>");
            try
            {
                DumpTriplesPlain(trips, writer, "<font color='green'>{0}</font>", graph);
            }
            catch (Exception e)
            {
                Warn(e);
                writer.WriteLine("<font color='red'>{0} {1} {2}</font>", e.GetType(), e.Message, e.StackTrace);
            }
            writer.WriteLine("</pre>");
        }
        private static void DumpTriplesPlain(IEnumerable<Triple> trips, TextWriter writer, string fmt, IGraph from)
        {
            var formatter = new SparqlFormatter/* TurtleW3CFormatter, TurtleFormatter*/(from.NamespaceMap);
            try
            {
                foreach (Triple trip0 in trips)
                {
                    DumpOneTriple(from, trip0, formatter, writer, fmt);
                }
            }
            catch (Exception e)
            {
                Warn(e);
                throw e;
            }
        }

        private static void DumpOneTriple(IGraph from, Triple trip0, ITripleFormatter formatter, TextWriter writer, string fmt)
        {
            Triple trip = trip0;
            if (trip.Graph == null)
            {
                trip = new Triple(trip.Subject, trip.Predicate, trip.Object, from);
                trip.Context = trip0.Context;
            }
            string ts;
            try
            {
                ts = trip.ToString(formatter);
            }
            catch (RdfOutputException)
            {
                ts = trip.ToString();
            }
            var hline = (" " + ts).Replace(" robokind:", " rk:");
            hline = hline.Replace("<robokind:", "<rk:");
            writer.WriteLine(fmt, hline);
        }

        public static void DocumentTerm(Term term, bool varnamesOnly)
        {
            bool newlyCreated;
            PredicateProperty pp = GraphWithDef.GetPredDef(term.name, term.Arity, out newlyCreated);
            int argNum = 0;
            foreach (Part part in term.ArgList)
            {
                argNum++;
                if (varnamesOnly) if (!(part is Variable)) continue;
                var argDef = GraphWithDef.GetAdef(pp, argNum, true);
                argDef.AddRangeTypeName(part.name);
            }
        }

        public List<string> GatherMts(string mt)
        {
            if (string.IsNullOrEmpty(mt)) return null;
            var gatherNames = new List<string>();
            if (mt.Contains(","))
            {
                foreach (var name in mt.Split(',', ' '))
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    gatherNames.Add(name);
                }
                return gatherNames;
            }
            if (mt == "*")
            {
                foreach (PNode p in KBGraph.SortedTopLevelNodes)
                {
                    gatherNames.Add(p.id);
                }
                return gatherNames;
            }
            return null;
        }

    }


}