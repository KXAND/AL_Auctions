using System.Globalization;
using UnityEngine;

namespace AuctionGame.Fusion
{
    public sealed class AuctionClientGui : MonoBehaviour
    {
        [SerializeField] private AuctionFusionSession session;
        private AuctionLocalSession localSession;

        private string _bidText = "75";
        private IAuctionPresentationSession _activeSession;

        private void Awake()
        {
            if (session == null)
            {
                session = GetComponent<AuctionFusionSession>();
            }

            if (localSession == null)
            {
                localSession = GetComponent<AuctionLocalSession>();
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(24, 24, 500, 640), GUI.skin.box);
            if (_activeSession == null)
            {
                DrawModeSelection();
                GUILayout.EndArea();
                return;
            }

            var session = _activeSession;
            var isLocalPlaytest = ReferenceEquals(session, localSession);
            GUILayout.Label(isLocalPlaytest ? "本地试玩" : "联网竞拍演示");
            GUILayout.Label(session.Status);

            var view = session.CurrentView;
            if (view == null)
            {
                if (isLocalPlaytest)
                {
                    if (GUILayout.Button("开始本地试玩"))
                    {
                        localSession.StartLocalPlaytest();
                    }
                }
                else
                {
                    DrawOnlineConnectButton();
                }

                GUILayout.EndArea();
                return;
            }

            if (isLocalPlaytest && GUILayout.Button("重置本地试玩"))
            {
                localSession.ResetLocalPlaytest();
                view = session.CurrentView;
            }

            if (view.IsWaitingForNextMatch)
            {
                GUILayout.Label("当前对局已开始，正在等待下一局");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"阶段：{ToChinesePhase(view.Phase)}");
            GUILayout.Label($"可用资产：{view.AvailableAssets}");
            GUILayout.Label($"倒计时：{Mathf.CeilToInt(view.RemainingSeconds)} 秒");
            GUILayout.Label(view.PublicClue);
            DrawHiddenGrid(view.GridWidth, view.GridHeight);

            if (!string.IsNullOrEmpty(view.PrivateClueResult))
            {
                GUILayout.Label($"我的私有线索：{view.PrivateClueResult}");
            }

            foreach (var knowledge in view.Knowledge)
            {
                var identity = knowledge.Name == null
                    ? "身份未揭示"
                    : knowledge.HasValue ? $"{knowledge.Name}，价值 {knowledge.Value}" : $"{knowledge.Name}，价值未揭示";
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

                foreach (var item in view.SettlementPackage)
                {
                    GUILayout.Label($"包裹真值：{item.Name}，价值 {item.Value}，位置({item.X},{item.Y})");
                }
            }

            if (view.RoundReveal != null)
            {
                GUILayout.Label($"第 {view.RoundReveal.Round} 轮揭示");
                GUILayout.Label(view.RoundReveal.WinnerSlot < 0
                    ? "本轮结果：未成交"
                    : $"本轮结果：席位 {view.RoundReveal.WinnerSlot + 1} 以 {view.RoundReveal.WinningBid} 成交");
                foreach (var seat in view.RoundReveal.Seats)
                {
                    var clue = string.IsNullOrEmpty(seat.ClueKind) ? "未选择" : seat.ClueKind;
                    GUILayout.Label($"席位 {seat.SeatIndex + 1}：出价 {seat.Bid}，线索 {clue}");
                }
            }

            GUILayout.EndArea();
        }

        private void DrawModeSelection()
        {
            GUILayout.Label("竞拍演示");
            GUILayout.Label("请选择验证方式");

            if (localSession != null && GUILayout.Button("开始本地试玩（不连接网络）"))
            {
                localSession.StartLocalPlaytest();
                _activeSession = localSession;
            }

            if (session != null)
            {
                DrawOnlineConnectButton();
            }
        }

        private void DrawOnlineConnectButton()
        {
            var previousGuiEnabled = GUI.enabled;
            GUI.enabled = session.CanStartClient;
            var buttonLabel = session.IsStarting
                ? "正在连接专用服务端..."
                : session.Status == "尚未连接" ? "连接本地专用服务端" : "重新连接本地专用服务端";
            if (GUILayout.Button(buttonLabel))
            {
                _activeSession = session;
                session.StartClient();
            }

            GUI.enabled = previousGuiEnabled;
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
                case "RoundReveal": return "回合揭示";
                case "Settlement": return "结算阶段";
                default: return "等待中";
            }
        }
    }
}
