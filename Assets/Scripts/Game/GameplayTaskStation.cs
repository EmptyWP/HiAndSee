using UnityEngine;

namespace HiAndSee.Game
{
    public class GameplayTaskStation : MonoBehaviour
    {
        [SerializeField] string stationName = "Task";
        [SerializeField] bool completed;

        public string StationName => stationName;
        public bool Completed => completed;

        public void SetGeneratedName(string value)
        {
            stationName = value;
            gameObject.name = value;
        }

        public void MarkCompleted()
        {
            completed = true;
        }

        public void MarkOpen()
        {
            completed = false;
        }
    }
}
