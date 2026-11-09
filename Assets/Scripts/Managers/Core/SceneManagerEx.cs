using UnityEngine.SceneManagement;

public class SceneManagerEx
{
    public Scene PendingScene { get; private set; } = Scene.Unknown;
    public Scene CurrentScene => ParseScene(SceneManager.GetActiveScene().name);

    public void Init()
    {
        PendingScene = CurrentScene;
    }

    public void ReloadCurrentScene()
    {
        Load(CurrentScene);
    }

    /// <summary>기존 매니저 상태와 풀 오브젝트를 정리한 뒤 지정한 씬을 로드한다.</summary>
    public void Load(Scene scene)
    {
        PendingScene = scene;
        Managers.Clear();
        SceneManager.LoadScene(ToSceneName(scene));
    }

    public bool IsCurrentScene(Scene scene)
    {
        return CurrentScene == scene;
    }

    private static string ToSceneName(Scene scene)
    {
        return scene.ToString();
    }

    private static Scene ParseScene(string sceneName)
    {
        if (System.Enum.TryParse(sceneName, out Scene scene))
            return scene;

        return Scene.Unknown;
    }
}
