using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;

public class PlayCanvasImporterWindow : EditorWindow {

    private string entityJsonPath = "";

    [MenuItem("Window/PlayCanvas Importer")]
    public static void ShowWindow() {
        GetWindow<PlayCanvasImporterWindow>("PlayCanvas Importer");
    }

    void OnGUI() {
        GUILayout.Label("Импорт сцены PlayCanvas", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        entityJsonPath = EditorGUILayout.TextField("Entity JSON Path:", entityJsonPath);
        if (GUILayout.Button("Выбрать", GUILayout.Width(80))) {
            entityJsonPath = EditorUtility.OpenFilePanel("Выберите entityData.json", "", "json");
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);
        if (GUILayout.Button("Импортировать сцену")) {
            ImportScene();
        }
    }

    void ImportScene() {
        if (string.IsNullOrEmpty(entityJsonPath)) {
            Debug.LogError("Файл сцены не указан.");
            return;
        }
        if (!File.Exists(entityJsonPath)) {
            Debug.LogError("Файл сцены не найден.");
            return;
        }
        Debug.Log($"Загрузка JSON-файла сцены: {entityJsonPath}");
        
        string jsonContent = File.ReadAllText(entityJsonPath);
        SceneData sceneData;
        try {
            sceneData = JsonConvert.DeserializeObject<SceneData>(jsonContent);
        } catch (Exception ex) {
            Debug.LogError($"Ошибка загрузки JSON-файла сцены: {ex.Message}");
            return;
        }
        
        if (sceneData == null || sceneData.root == null) {
            Debug.LogError("Ошибка загрузки JSON-файла сцены.");
            return;
        }
        
        Debug.Log($"Создание корневого объекта сцены: {sceneData.root.name}");
        GameObject rootObject = new GameObject(sceneData.root.name);
        CreateGameObjectHierarchy(sceneData.root, rootObject);
        Debug.Log("Импорт сцены завершён.");
    }

    void CreateGameObjectHierarchy(Entity entity, GameObject parent) {
        Debug.Log("Создание объекта: " + entity.name);
        GameObject obj = new GameObject(entity.name);
        obj.transform.SetParent(parent.transform);
        obj.transform.localScale = Vector3.one;

        if (entity.position != null && entity.position.Count == 3) {
            obj.transform.position = new Vector3(entity.position[0], entity.position[1], entity.position[2]);
            Debug.Log("Установлена позиция: " + obj.name + " → " + obj.transform.position);
        }

        if (entity.rotation != null && entity.rotation.Count == 3) {
            Quaternion playCanvasRotation = Quaternion.Euler(
                entity.rotation[0],
                -entity.rotation[2],
                -entity.rotation[1]
            );
            Quaternion axisCorrection = Quaternion.Euler(90, 0, 0);
            obj.transform.rotation = axisCorrection * playCanvasRotation;
            Debug.Log($"Установлено вращение для {obj.name}: {obj.transform.rotation.eulerAngles}");
        }

        if (entity.scale == null || entity.scale.Count < 3) {
            entity.scale = new List<float> { 1f, 1f, 1f };
        }
        if (entity.components != null) {
            AddComponents(obj, entity.components, entity.scale);
        }
        foreach (var child in entity.children) {
            CreateGameObjectHierarchy(child, obj);
        }
    }

    void AddComponents(GameObject obj, Dictionary<string, object> components, List<float> scale) {
        if (components.ContainsKey("camera")) {
            obj.AddComponent<Camera>();
            Debug.Log($"Добавлена камера в объект: {obj.name}");
        }

        if (components.ContainsKey("light")) {
            var lightDataRaw = components["light"];
            var lightData = lightDataRaw as Dictionary<string, object> ?? JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(lightDataRaw));
            Debug.Log($"Обнаружены данные освещения для {obj.name}: {JsonConvert.SerializeObject(lightData, Formatting.Indented)}");
            
            if (lightData != null) {
                var lightComponent = obj.AddComponent<Light>();
                //MonoBehaviour bakeryLight = null;

                int shape = lightData.ContainsKey("shape") ? Convert.ToInt32(lightData["shape"]) : 0;

                if (lightData.ContainsKey("type")) {
                    string lightType = lightData["type"].ToString().ToLower();
                    Single intensity = Convert.ToSingle(lightData["intensity"]);

                    List<float> colorArray = (lightData["color"] as Newtonsoft.Json.Linq.JArray)?.ToObject<List<float>>();
                    Color color = new Color(colorArray[0], colorArray[1], colorArray[2]);

                    lightComponent.intensity = intensity;
                    lightComponent.color = color;


                    switch (lightType) {
                        case "directional":
                            lightComponent.type = LightType.Directional;
                            BakeryDirectLight bDirectLight = obj.AddComponent<BakeryDirectLight>() as BakeryDirectLight;

                            bDirectLight.color = color;
                            bDirectLight.intensity = intensity;
                            break;
                        case "spot":
                            switch (shape) {
                                case 0:
                                    Single outerConeAngle = Convert.ToSingle(lightData["outerConeAngle"]) * 2.0f;
                                    Single innerConeAngle= Convert.ToSingle(lightData["innerConeAngle"])* 2.0f;

                                    lightComponent.type = LightType.Spot;
                                    BakeryPointLight bSpotLight = obj.AddComponent<BakeryPointLight>() as BakeryPointLight;
                                    bSpotLight.projMode = BakeryPointLight.ftLightProjectionMode.Cone;
                                    
                                    bSpotLight.color = color;
                                    bSpotLight.intensity = intensity;

                                    Single innerPercent = innerConeAngle * 100.0f / outerConeAngle;

                                    bSpotLight.innerAngle = innerPercent;
                                    bSpotLight.angle = outerConeAngle;


                                    break;
                                case 1:
                                    lightComponent.areaSize = new Vector2(scale[0], scale[2]);
                                    lightComponent.type = LightType.Rectangle;
                                    BakeryLightMesh bRectangleLight = obj.AddComponent<BakeryLightMesh>() as BakeryLightMesh;

                                    bRectangleLight.color = color;
                                    bRectangleLight.intensity = intensity;

                                    break;
                                case 2:
                                    lightComponent.areaSize = new Vector2(scale[0], scale[2]);
                                    lightComponent.type = LightType.Disc;
                                    BakeryLightMesh bDiscLight = obj.AddComponent<BakeryLightMesh>() as BakeryLightMesh;
                                    bDiscLight.color = color;
                                    bDiscLight.intensity = intensity;

                                    break;
                                default:
                                    Debug.LogWarning($"Неизвестная форма: {shape}");
                                    break;
                            }
                            break;
                        case "point":
                            lightComponent.type = LightType.Point;
                            BakeryPointLight bPointLight = obj.AddComponent<BakeryPointLight>() as BakeryPointLight;
                            bPointLight.color = color;
                            bPointLight.intensity = intensity;
                            break;
                        default:
                            Debug.LogError($"{obj.name}: Неизвестный тип источника света!");
                            break;
                    }
                    Debug.Log("Добавлен Bakery-компонент для " + lightComponent.type);
                }
            }
            Debug.Log($"Добавлен источник света в объект: {obj.name}");
        }
    }
}

[System.Serializable]
public class SceneData {
    public Entity root;
}

[System.Serializable]
public class Entity {
    public string id;
    public string name;
    public List<float> position;
    public List<float> rotation;
    public List<float> scale;
    public Dictionary<string, object> components;
    public List<Entity> children;
}
