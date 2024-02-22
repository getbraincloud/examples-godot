using Godot;
using Godot.Collections;
using System;

public partial class Statistics : VBoxContainer
{
    private Label _statisticsHeader;
    private VBoxContainer _statisticsContainers;
    private Label _errorMsg;

    private BCManager _brainCloud;

    private bool _isGlobalStatistic;

    public override void _Ready()
    {
        _statisticsHeader = GetNode<Label>("HBoxContainer/StatisticHeader");
        _statisticsContainers = GetNode<VBoxContainer>("ScrollContainer/StatisticsContainers");
        _errorMsg = GetNode<Label>("ErrorMsg");

        _brainCloud = GetNode<BCManager>("/root/BCManager");

        _errorMsg.Hide();

        _brainCloud.Connect(BCManager.SignalName.ReceivedStatistics, new Callable(this, MethodName.RefreshStatistics));
    }

    public void SetStatisticType(bool isGlobalStatistic)
    {
        _isGlobalStatistic = isGlobalStatistic;

        if (_isGlobalStatistic)
        {
            _statisticsHeader.Text = "Global Statistics";
            _brainCloud.ReadAllGlobalStats();
        }
        else
        {
            _statisticsHeader.Text = "Player Statistics";
            _brainCloud.ReadAllPlayerStats();
        }
    }

    private void RefreshStatistics(Dictionary statistics)
    {
        _errorMsg.Hide();

        if (_statisticsContainers.GetChildCount() > 0)
        {
            for (int i = 0; i < _statisticsContainers.GetChildCount(); i++)
            {
                _statisticsContainers.GetChild(i).QueueFree();
            }
        }
        if (statistics.Keys.Count > 0)
        {
            foreach (var key in statistics.Keys)
            {
                string statisticName = key.ToString();
                int statisticValue = (int)statistics[key];

                var statisticsContainer = GD.Load<PackedScene>("res://statistic_container.tscn");

                StatisticContainer statistic = (StatisticContainer)statisticsContainer.Instantiate();

                _statisticsContainers.AddChild(statistic);

                statistic.SetStatisticName(statisticName);
                statistic.SetStatisticValue(statisticValue);

                statistic.Connect(StatisticContainer.SignalName.StatisticIncremented, new Callable(this, MethodName.OnStatisticIncremented));
            }
        }

        else
        {
            _errorMsg.Show();
            GD.Print("No Statistics found");
        }
    }

    private void OnStatisticIncremented(string statisticName, int incrementAmount)
    {
        if (_isGlobalStatistic)
        {
            _brainCloud.IncrementGlobalStatistics(statisticName, incrementAmount);
        }

        else
        {
            _brainCloud.IncrementPlayerStatistics(statisticName, incrementAmount);
        }
    }
}
