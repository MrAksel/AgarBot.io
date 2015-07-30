using AgarBot;
using AgarBot.PackageManagers;
using System;
using System.Windows.Forms;

namespace AgarSampleClient
{
    public partial class MainForm : Form
    {
        AgarClient client;
        DefaultPacketManager pm;

        public MainForm()
        {
            InitializeComponent();

            pm = new DefaultPacketManager();
            client = new AgarClient(pm);
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            await pm.Start();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            client.StartProcessingLoop();
            client.SendInitializationPackets();
        }

        private async void button3_Click(object sender, EventArgs e)
        {
            await pm.Stop();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            client.StopProcessingLoop();
        }
    }
}
