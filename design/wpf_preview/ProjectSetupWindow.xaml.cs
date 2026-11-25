using System.Windows;

namespace OnSitePreview;

public partial class ProjectSetupWindow : Window
{
    public ProjectSetupWindow() => InitializeComponent();

    // 미리보기용: 잘못된 값 상태 (빨간 한 줄 + [확인] 비활성)
    public void ShowInvalidSample()
    {
        Cycle.Text = "0";
        CycleError.Visibility = Visibility.Visible;
        OkButton.IsEnabled = false;
    }
}
