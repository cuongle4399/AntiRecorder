using System;

namespace WindowsSecureBrowser.Privacy
{
    public class PrivateModeManager
    {
        public bool IsPrivateModeEnabled { get; private set; }

        public void EnablePrivateMode()
        {
            IsPrivateModeEnabled = true;
        }

        public void DisablePrivateMode()
        {
            IsPrivateModeEnabled = false;
            SecureClearManager.ClearSensitiveData();
        }
    }
}
