using System;
using System.Xml;
using System.Text;
using AltAIMLbot;
using AltAIMLbot.Utils;
using AltAIMLParser;
using RTParser.Utils;

namespace RTParser.AIMLTagHandlers
{
    /// <summary>
    /// The sr element is a shortcut for: 
    /// 
    /// <srai><star/></srai> 
    /// 
    /// The atomic sr does not have any content. 
    /// </summary>
    public class sr : RTParser.Utils.AIMLTagHandler
    {
        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="bot">The bot involved in this request</param>
        /// <param name="user">The user making the request</param>
        /// <param name="query">The query that originated this node</param>
        /// <param name="request">The request inputted into the system</param>
        /// <param name="result">The result to be passed to the user</param>
        /// <param name="templateNode">The node to be Processed</param>
        public sr(RTParser.AltBot bot,
                        User user,
                        SubQuery query,
                        Request request,
                        Result result,
                        XmlNode templateNode)
            : base(bot, user, query, request, result, templateNode)
        {
            IsDeterministic = false;
        }

        protected override Unifiable ProcessChange()
        {
            if (this.templateNode.Name.ToLower() == "sr")
            {
                Unifiable starContent = GetStarContent();
                bool starFailed = IsNull(starContent);
                if (starFailed)
                {
                    return Failure("<SR>");
                }
                return callSRAI(starContent);
            }
            return Unifiable.Empty;
        }
    }
}
