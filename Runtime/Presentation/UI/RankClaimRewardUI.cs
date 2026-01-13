using System;
using TMPro;
using TrippleQ.AvatarSystem;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RankClaimRewardUI : MonoBehaviour
    {
        [SerializeField] AvatarItemView _avatar;
        [SerializeField] TMP_Text _nameText;

        internal void RenderData(Sprite icon, string name)
        {
            _avatar.UpDateAvatar(icon);
            _nameText.text = name;
        }
    }
}
