using Gamemode_Lib.Events;
using PropHunt.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PropHunt;

public class CustomRoundTimer : MonoBehaviour
{
    public void Awake()
    {
        StartTimer();
    }

    private void StartTimer()
    {
        CourseManager.Instance.countdownRemainingTime = ConfigurationHandler.Instance.RoundTimerLengthInSeconds;
        if (CourseManager.Instance.matchEndCountdownRoutine != null)
            CourseManager.Instance.StopCoroutine(CourseManager.Instance.matchEndCountdownRoutine);
        CourseManager.Instance.matchEndCountdownRoutine = CourseManager.Instance.StartCoroutine(CourseManager.Instance.CountDownToMatchEndRoutine());
    }
    
}