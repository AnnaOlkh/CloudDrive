using System.Windows;
using System.Windows.Media;
using CloudDrive.Client;

namespace CloudDrive.Desktop;

public partial class SyncResultWindow : Window
{
    public SyncResultWindow(SyncExecutionResult result, string localPath, string remotePath)
    {
        InitializeComponent();

        var transferred = result.Completed.Count(x => x.ConfirmedHash is not null);
        var deleted = result.Completed.Count(x => x.ConfirmedHash is null);
        var pending = Math.Max(0, result.Plan.ToDeleteLocal.Count + result.Plan.ToDeleteRemote.Count - deleted);
        var conflicts = result.Plan.Conflicts.Count;
        var failed = result.Failures.Count;

        var problems = conflicts > 0 || failed > 0;
        StatusIcon.Text = problems ? "\uE7BA" : "\uE930";
        StatusIcon.Foreground = (Brush)FindResource(problems ? "Warning" : "Success");
        TitleText.Text = failed > 0 ? "Synchronization finished with errors"
            : conflicts > 0 ? "Synchronization finished with conflicts"
            : transferred + deleted == 0 ? "Everything is already in sync"
            : "Synchronization complete";
        SubtitleText.Text = $"{localPath}  ↔  {remotePath}";

        TransferredText.Text = transferred.ToString();
        DeletedText.Text = deleted.ToString();
        PendingText.Text = pending.ToString();
        ConflictText.Text = conflicts.ToString();
        FailedText.Text = failed.ToString();

        if (conflicts > 0)
        {
            ConflictList.ItemsSource = result.Plan.Conflicts.ToList();
            ConflictSection.Visibility = Visibility.Visible;
        }
        if (failed > 0)
        {
            FailureList.ItemsSource = result.Failures.Select(x => $"{x.Key} – {x.Value}").ToList();
            FailureSection.Visibility = Visibility.Visible;
        }
    }
}
