using System;
using System.Windows.Forms;

namespace AgarBot.UI
{
    public partial class MainForm : Form
    {
        AgarClient client;
        AgarPacketManager pm;

        public MainForm()
        {
            InitializeComponent();

            pm = new AgarPacketManager();
            client = new AgarClient(pm);
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            await pm.Start();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            client.StartInitialization();
        }
    }
}
