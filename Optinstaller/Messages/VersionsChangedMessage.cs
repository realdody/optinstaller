using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Optinstaller.Messages;

public class VersionsChangedMessage : ValueChangedMessage<bool>
{
    public VersionsChangedMessage(bool value) : base(value)
    {
    }
}
