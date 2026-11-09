using System;
using UnityEngine;

namespace RestoreCastle3D
{
    public enum CastleCreekOutcome3D
    {
        None,
        StageCleared,
        CampaignCleared,
        ScoreFailed,
        PollutionFailed
    }

    /// <summary>
    /// Pure progression state for the timed creek game. Stage targets refer to the
    /// existing cumulative creek score; pollution persists between all stages.
    /// </summary>
    public sealed class CastleCreekProgress3D
    {
        public const int CampaignStages = 5;
        public static readonly int[] DefaultTargets = { 5, 15, 30, 50, 75 };

        private readonly int[] _targets;
        private readonly float _duration;
        private readonly int _pollutionPerPiece;
        private readonly int _pollutionLimit;

        public int StageIndex { get; private set; }
        public int Pollution { get; private set; }
        public float RemainingSeconds { get; private set; }
        public bool IsEndless { get; private set; }
        public bool IsGameOver { get; private set; }
        public CastleCreekOutcome3D LastOutcome { get; private set; }

        public int StageNumber => Mathf.Min(StageIndex + 1, CampaignStages);
        public int TargetScore => IsEndless ? 0 : _targets[StageIndex];
        public int PollutionLimit => _pollutionLimit;
        public float StageDuration => _duration;

        public CastleCreekProgress3D(float stageDuration, int[] targets,
            int pollutionPerPiece, int pollutionLimit)
        {
            _duration = stageDuration > 0f ? stageDuration : 30f;
            _targets = targets != null && targets.Length == CampaignStages
                ? (int[])targets.Clone() : (int[])DefaultTargets.Clone();

            // A malformed Inspector value must never make a stage impossible to interpret.
            for (int i = 0; i < _targets.Length; i++)
                _targets[i] = Mathf.Max(i == 0 ? 1 : _targets[i - 1] + 1, _targets[i]);

            _pollutionPerPiece = Mathf.Max(1, pollutionPerPiece);
            _pollutionLimit = Mathf.Max(1, pollutionLimit);
            RemainingSeconds = _duration;
        }

        public CastleCreekOutcome3D Tick(float deltaTime, int totalScore)
        {
            if (IsGameOver || IsEndless) return CastleCreekOutcome3D.None;
            RemainingSeconds = Mathf.Max(0f, RemainingSeconds - Mathf.Max(0f, deltaTime));
            if (RemainingSeconds > 0f) return CastleCreekOutcome3D.None;

            if (totalScore < TargetScore)
            {
                IsGameOver = true;
                return LastOutcome = CastleCreekOutcome3D.ScoreFailed;
            }

            StageIndex++;
            if (StageIndex >= CampaignStages)
            {
                IsEndless = true;
                return LastOutcome = CastleCreekOutcome3D.CampaignCleared;
            }

            RemainingSeconds = _duration;
            return LastOutcome = CastleCreekOutcome3D.StageCleared;
        }

        /// <summary>
        /// Caller passes only individual trash pieces actually past the Z=-15 scoring
        /// boundary. They are counted once when their connected group is settled.
        /// </summary>
        public CastleCreekOutcome3D CountTrash(int pieces)
        {
            if (pieces <= 0 || IsGameOver) return CastleCreekOutcome3D.None;
            long increase = (long)pieces * _pollutionPerPiece;
            Pollution = (int)Math.Min(_pollutionLimit, Pollution + increase);
            if (Pollution < _pollutionLimit) return CastleCreekOutcome3D.None;
            IsGameOver = true;
            return LastOutcome = CastleCreekOutcome3D.PollutionFailed;
        }
    }
}
