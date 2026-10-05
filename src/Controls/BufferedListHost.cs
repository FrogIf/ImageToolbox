using System.Drawing;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 普通 ListBox 在失焦（例如点开右侧操作面板里的下拉按钮）时会先把整个客户区擦成背景色再重绘，
    // 左侧功能列表较高时就会整体闪一下。这里用一层带 WS_EX_COMPOSITED 的宿主面板把列表包起来：
    // 面板及其子控件（ListBox）会先在缓冲区里自下而上绘制好再一次性上屏，擦除中间态不会显示出来，
    // 从而消除那次整块闪烁。注意不能把 WS_EX_COMPOSITED 直接加在原生 LISTBOX 窗口类上（创建句柄会失败）。
    internal class BufferedListHost : Panel
    {
        public BufferedListHost()
        {
            BackColor = SystemColors.Window;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }
    }
}
