using System.Globalization;
using UnityEngine;

namespace AuctionGame.Fusion
{
    public sealed class AuctionClientGui : MonoBehaviour
    {
        [SerializeField] private AuctionFusionSession session;

        private string _bidText = "75";

        private void Awake()
        {
            if (session == null)
            {
                session = GetComponent<AuctionFusionSession>();
            }
        }

        private void OnGUI()
        {
            if (session == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(24, 24, 500, 640), GUI.skin.box);
            GUILayout.Label("联网竞拍演示");
            GUILayout.Label(session.Status);

            var view = session.CurrentView;
            if (view == null)
            {
                if (GUILayout.Button("连接本地专用服务端"))
                {
                    session.StartClient();
                }

                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"阶段：{ToChinesePhase(view.Phase)}");
            GUILayout.Label($"可用资产：{view.AvailableAssets}");
            GUILayout.Label(view.PublicClue);
            DrawHiddenGrid(view.GridWidth, view.GridHeight);

            if (!string.IsNullOrEmpty(view.PrivateClueResult))
            {
                GUILayout.Label($"我的私有线索：{view.PrivateClueResult}");
            }

            foreach (var knowledge in view.Knowledge)
            {
                var identity = knowledge.Name == null ? "身份未揭示" : $"{knowledge.Name}，价值 {knowledge.Value}";
                GUILayout.Label($"已知：{knowledge.Rarity}，位置({knowledge.X},{knowledge.Y})，尺寸 {knowledge.Width}×{knowledge.Height}，{identity}");
            }

            if (view.Phase == "Analysis")
            {
                foreach (var choice in view.PrivateClueChoices)
                {
                    if (GUILayout.Button(choice.Label))
                    {
                        session.SelectPrivateClue(choice.Id);
                    }
                }
            }

            if (view.Phase == "Bidding")
            {
                GUILayout.Label("整数出价（0 表示放弃竞拍）");
                _bidText = GUILayout.TextField(_bidText, 8);
                if (GUILayout.Button("提交出价") && int.TryParse(_bidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                {
                    session.SubmitBid(amount);
                }
            }

            if (view.Phase == "Settlement")
            {
                GUILayout.Label(view.WinnerSlot < 0
                    ? "本轮未成交"
                    : $"席位 {view.WinnerSlot + 1} 以 {view.WinningBid} 成交");
            }

            GUILayout.EndArea();
        }

        private static void DrawHiddenGrid(int width, int height)
        {
            GUILayout.Label("包裹网格（默认不展示藏品真值）");
            for (var row = 0; row < height; row++)
            {
                GUILayout.BeginHorizontal();
                for (var column = 0; column < width; column++)
                {
                    GUILayout.Box("?", GUILayout.Width(32), GUILayout.Height(32));
                }

                GUILayout.EndHorizontal();
            }
        }

        private static string ToChinesePhase(string phase)
        {
            switch (phase)
            {
                case "Analysis": return "分析阶段";
                case "Bidding": return "出价阶段";
                case "Settlement": return "结算阶段";
                default: return "等待中";
            }
        }
    }
}
