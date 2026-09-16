namespace ShowroomBot.Windows;

public interface IIdleDetector
{
    TimeSpan GetIdleTime();
}
