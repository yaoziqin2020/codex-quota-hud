using CodexQuotaHud.Skins.Contracts;
using Microsoft.Win32;

namespace CodexQuotaHud.SkinDesigner.Images;

public interface IImagePicker
{
    string? ChooseImage(SkinAssetSlot slot);
}

public sealed class WindowsImagePicker : IImagePicker
{
    public string? ChooseImage(SkinAssetSlot slot)
    {
        if (!Enum.IsDefined(slot))
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            CheckPathExists = true,
            DereferenceLinks = false,
            Multiselect = false,
            Filter = slot == SkinAssetSlot.Decoration
                ? "PNG image (*.png)|*.png"
                : "PNG or JPEG image (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
