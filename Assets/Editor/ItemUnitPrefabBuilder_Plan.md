# ItemUnit Prefab Builder Plan

## 1) Muc tieu
- Xay dung Unity Editor Tool (khong dung ben thu 3) de tao prefab ItemUnit nhanh, dung cau truc thong nhat.
- Tool phai tao duoc skeleton hierarchy:
  - gameObjectRoot: ten item can tao
  - child Root: game object rong de dat model (model la con cua node nay)
  - child Virsual: game object rong de dat visual 3D (prefab option + offset)
- Core component gan tren root la class ke thua ItemUnit.
- Sub-component gan tren root la cac class ke thua BaseComponent.
- Tu dong wiring cac tham chieu co ban de prefab co the dung ngay.

## 2) Pham vi MVP
- EditorWindow tao prefab don.
- Chon core type (ItemUnit-derived).
- Core Builder:
  - Sau khi chon core type, tool tao/giu preview component tam de hien thi SerializedObject cua core.
  - Cho phep chinh truc tiep serialized field cua core ngay trong tool.
  - Khong hien/copy cac field auto-wire nhu _components.
- Chon danh sach sub-component type (BaseComponent-derived).
- Sub-components Builder:
  - Moi sub-component trong list co foldout Setting rieng.
  - Sau khi chon type, tool tao/giu preview component tam de hien thi SerializedObject cua sub-component.
  - Cho phep chinh truc tiep serialized field cua tung sub-component ngay trong tool.
  - Khong hien/copy cac field auto-wire nhu CacheTransform/poolEntity.
- Du lieu Serializable duoc cap nhat ngay trong tool va duoc apply vao prefab khi build.
- Co the dung mot prefab co cau truc tuong tu de copy Component data:
  - Chi copy du lieu tho va tham chieu asset.
  - Khong copy cac tham chieu local trong prefab/source hierarchy.
- Chon visual prefab (optional) + local transform offset.
- Phan Virsual ho tro an/hien Setting component de tiet kiem khong gian UI.
- Tu dong tao hierarchy Root/Virsual.
- Tu dong gan _components trong ItemUnit.
- Tu dong set CacheTransform/poolEntity cho BaseComponent.
- Save prefab vao folder chi dinh.

## 3) Kien truc de xuat
- File 1: Assets/Editor/ItemUnitBuilder/ItemUnitPrefabBuilderWindow.cs
  - UI nhap thong tin tao prefab.
  - Nut Validate, Build, Ping Asset.
- File 2: Assets/Editor/ItemUnitBuilder/ItemUnitPrefabBuilderService.cs
  - Chiu trach nhiem tao hierarchy, add component, auto-wire, save prefab.
- File 3: Assets/Editor/ItemUnitBuilder/ItemUnitBuilderValidation.cs
  - Rule validate type, du lieu dau vao, duong dan output.
- File 4: Assets/Editor/ItemUnitBuilder/ItemUnitBuilderPreset.cs
  - ScriptableObject preset de tai su dung cau hinh tao prefab.

## 4) Data model cho builder
- prefabName: string
- outputFolder: string
- coreItemUnitType: System.Type (ItemUnit-derived)
- subComponentTypes: List<System.Type> (BaseComponent-derived)
- sourcePrefab: GameObject (optional, dung de prefill/copy component data)
- corePreviewData: SerializedObject/Component preview tam
- subComponentPreviewData: List<SerializedObject/Component preview tam>
- visualPrefab: GameObject (optional)
- visualLocalPosition: Vector3
- visualLocalRotationEuler: Vector3
- visualLocalScale: Vector3
- createRootNode: bool (default true)
- createVirsualNode: bool (default true)
- overwriteIfExists: bool

## 5) Flow Build prefab
1. Validate input.
2. Tao root GO theo prefabName.
3. Tao child Root va Virsual.
4. Gan core ItemUnit component vao root.
5. Apply serialized data tu core preview vao core component, neu co.
6. Gan cac sub-component vao root.
7. Apply serialized data tu sub-component preview vao tung sub-component, neu co.
8. Neu sourcePrefab co gia tri thi dung de prefill/copy data vao preview truoc khi build hoac copy truc tiep neu preview chua co data.
9. Neu visualPrefab co gia tri:
   - Instantiate prefab vao duoi Virsual.
   - Apply local transform offset theo input.
10. Auto-wire:
   - Voi moi BaseComponent:
     - CacheTransform = root.transform
     - poolEntity = core ItemUnit instance
   - ItemUnit._components = danh sach component MonoBehaviour duoc chon
11. Save as prefab qua PrefabUtility.SaveAsPrefabAsset.
12. Destroy temporary GO trong scene editor.
13. Ping asset vua tao.

## 6) Rule validate
- prefabName khong rong.
- outputFolder hop le va nam trong Assets.
- coreItemUnitType phai ke thua ItemUnit.
- subComponentTypes moi phan tu phai ke thua BaseComponent.
- Khong cho duplicate sub-component type (tru khi class cho phep multi-instance).
- Neu animation/visual required ma visualPrefab null thi warning.
- Neu prefab ton tai:
  - overwriteIfExists = false => chan build + thong bao.

## 7) Auto-wiring chi tiet
- BaseComponent fields:
  - CacheTransform: set root.transform
  - poolEntity: set ve core ItemUnit component
- ItemUnit fields:
  - _components: set list sub-component theo thu tu hien thi UI
- Auto-wire tham chieu kieu ro rang (optional, an toan):
  - Field type la HitComponent thi tim component cung type tren root va gan neu null
  - Tuong tu HealthComponent, EffectComponent...
- Chi set field khi field dang null de tranh ghi de data co y.

## 8) UX trong EditorWindow
- Section Basic:
  - Prefab Name
  - Output Folder
- Section Core:
  - Dropdown chon ItemUnit-derived type
  - Foldout Setting de chinh serialized field cua core component preview.
- Section Sub-components:
  - Reorderable list chon BaseComponent-derived type
  - Moi sub-component co foldout Setting de chinh serialized field cua preview component.
- Section Visual:
  - Visual Prefab (optional)
  - Foldout Setting component de an/hien Local Position / Rotation / Scale
- Section Actions:
  - Validate
  - Build Prefab
  - Build and Select
- Section Log:
  - Hien thi warnings, errors, va ket qua wiring.

## 9) Kha nang mo rong sau MVP
- Batch build tu nhieu preset.
- Tao bien the LV1/LV2/LV3 theo naming template.
- Nut Repair Prefab de fix lai wiring cho prefab cu.
- Rule packs theo loai item (Tower, Obstacle, Currency...) de prefill component list.

## 10) Test checklist
- Tao prefab chi co core ItemUnit.
- Tao prefab co nhieu BaseComponent va verify _components.
- Verify BaseComponent duoc set CacheTransform/poolEntity dung.
- Verify visual prefab duoc parent vao node Virsual va dung offset.
- Verify save path dung va ping asset thanh cong.
- Verify overwrite behavior.
- Verify prefab tao ra co the Initialize trong gameplay khong null ref.

## 11) Definition of Done
- Tool tao duoc prefab theo dung skeleton root/Root/Virsual.
- Core + sub-component duoc add va wire dung.
- Khong can thu cong set lai cac field co ban sau khi build.
- Chay thu oc trong Unity Editor khong loi compile.
- Khong dung package ben thu 3.

## 12) Roadmap trien khai de xuat
- Phase 1: Scaffold EditorWindow + Service + Validation.
- Phase 2: Build flow tao prefab + save.
- Phase 3: Auto-wire BaseComponent + ItemUnit._components.
- Phase 4: Preset ScriptableObject + load/save preset.
- Phase 5: Hoan thien UX + log + test checklist.
