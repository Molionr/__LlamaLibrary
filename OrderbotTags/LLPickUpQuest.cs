using Clio.XmlEngine;
using ff14bot.RemoteWindows;
using System.Collections.Generic;
using System.ComponentModel;
using TreeSharp;
using Action = TreeSharp.Action;

namespace LlamaLibrary.OrderbotTags
{
    [XmlElement("LLPickUpQuest")]
    [XmlElement("LLPickupQuest")]
    public class LLPickUpQuest : PickupQuestTag
    {
        [DefaultValue(new int[0])]
        [XmlAttribute("DialogOption")]
        public int[] DialogOption { get; set; }

        private readonly Queue<int> selectStringIndex = new Queue<int>();

        protected override void OnStart()
        {
            if (DialogOption.Length > 0)
            {
                foreach (var i in DialogOption) { selectStringIndex.Enqueue(i); }
            }

            base.OnStart();
        }

        protected override Composite CreateBehavior()
        {
            return new PrioritySelector(
                new Decorator(ret => SelectString.IsOpen,
                    new Action(r =>
                    {
                        if (selectStringIndex.Count > 0) { SelectString.ClickSlot((uint)selectStringIndex.Dequeue()); }
                        else { SelectString.ClickSlot(0); }
                    })
                ),
				new Decorator(ret => SelectYesno.IsOpen,
                    new Action(r =>
                    {
                        SelectYesno.ClickYes();
                    })
                ),
                base.CreateBehavior()
            );
        }
    }
}
