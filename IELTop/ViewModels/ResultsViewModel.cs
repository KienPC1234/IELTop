using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;

namespace IELTop.ViewModels;

/// <summary>
/// Past mock test results with band ranges and the official criteria tables.
/// </summary>
public sealed partial class ResultsViewModel : ObservableObject
{
    [ObservableProperty] private string _statusMessage = "Your finished tests appear here.";

    public ObservableCollection<ExamAttempt> Attempts { get; } = new();
    public ObservableCollection<string> WritingTable { get; } = new();
    public ObservableCollection<string> SpeakingTable { get; } = new();

    public ResultsViewModel()
    {
        foreach (var line in IeltsBanding.WritingBandTable()) WritingTable.Add(line);
        foreach (var line in IeltsBanding.SpeakingBandTable()) SpeakingTable.Add(line);
        Load();
    }

    public bool HasAttempts => Attempts.Count > 0;
    public string SummaryLabel => HasAttempts
        ? $"{Attempts.Count} test(s) finished. Bands are practice estimates, not official scores."
        : "No finished tests yet. Run a mock test first.";

    public string CriteriaHint => IeltsBanding.WritingCriteriaHint + " " + IeltsBanding.SpeakingCriteriaHint;

    [RelayCommand]
    private void Load()
    {
        Attempts.Clear();
        try
        {
            using var db = new AppDbContext();
            foreach (var a in db.ExamAttempts.OrderByDescending(x => x.CreatedAt).Take(100).ToList())
                Attempts.Add(a);
        }
        catch (Exception)
        {
            StatusMessage = "Could not read past results. The local database may be locked.";
            return;
        }
        StatusMessage = HasAttempts ? SummaryLabel : "No finished tests yet. Run a mock test first.";
        OnPropertyChanged(nameof(HasAttempts));
        OnPropertyChanged(nameof(SummaryLabel));
    }

    [RelayCommand]
    private void ClearAll()
    {
        try
        {
            using var db = new AppDbContext();
            db.ExamAttempts.RemoveRange(db.ExamAttempts);
            db.SaveChanges();
        }
        catch (Exception)
        {
            StatusMessage = "Could not clear results. Try again.";
            return;
        }
        Load();
    }
}
