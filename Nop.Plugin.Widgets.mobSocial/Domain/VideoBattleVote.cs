﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mob.Core.Domain;
using Nop.Core;
using Nop.Plugin.Widgets.MobSocial.Enums;

namespace Nop.Plugin.Widgets.MobSocial.Domain
{
    public class VideoBattleVote: BaseMobEntity
    {
        public int UserId { get; set; }

        public int VideoBattleId { get; set; }

        public int ParticipantId { get; set; }

        public int VoteValue { get; set; }

        public VideoBattleVoteStatus VoteStatus { get; set; }

        public virtual VideoBattle VideoBattle { get; set; }
    }
}
