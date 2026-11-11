# Лабораторна робота: K-Means на PARCS

Послідовна та паралельна сегментація JPG/PNG у просторах RGB (3D) і RGB з координатами пікселів (5D). Реалізація: [`Parcs.Modules.KMeansSegmentation`](../../modules/Parcs.Modules.KMeansSegmentation/).

Паралельний модуль один раз розсилає частини зображення воркерам. На кожній ітерації воркери отримують центроїди й повертають суми ознак та кількості пікселів у кластерах. Керуючий модуль оновлює центроїди. Наприкінці воркери обчислюють мітки за фінальними центрами, а керуючий модуль формує зображення.

## 1. Локальна перевірка

Потрібні Docker Desktop у режимі Linux-контейнерів і .NET 10 SDK. Команди PowerShell виконуються з кореня `parcs8`.

Запустіть локальний PARCS: Host API, Portal, PostgreSQL і 8 воркерів.

```powershell
docker compose -f .\infra\aws-ec2-static\docker-compose.local.yml up -d --build
docker compose -f .\infra\aws-ec2-static\docker-compose.local.yml ps
```

Відкрийте [Portal](http://localhost:8080) і виконайте кроки наступного розділу. Для першої перевірки використайте невелике фото та послідовний модуль, потім паралельний із `PointsNumber=1`.

Логи та зупинка зі збереженням даних:

```powershell
docker compose -f .\infra\aws-ec2-static\docker-compose.local.yml logs --tail 100
docker compose -f .\infra\aws-ec2-static\docker-compose.local.yml stop
```

Для повторного запуску виконайте ту саму команду `up -d`.

## 2. Збірка та запуск модуля

Ці кроки однакові для локального PARCS і хмарного кластера.

```powershell
dotnet publish .\modules\Parcs.Modules.KMeansSegmentation\Parcs.Modules.KMeansSegmentation.csproj `
    -c Release -o .\kmeans-module-publish
```

1. У Portal відкрийте **Modules → New Module** і завантажте `Parcs.Modules.KMeansSegmentation.dll` та `Parcs.Modules.KMeansSegmentation.deps.json` із [`kmeans-module-publish`](../../kmeans-module-publish/).
2. Створіть Job для цієї збірки, додайте своє фото JPG/PNG і виберіть клас:
   - послідовний: `Parcs.Modules.KMeansSegmentation.Sequential.SequentialKMeansMainModule`;
   - паралельний: `Parcs.Modules.KMeansSegmentation.Parallel.ParallelKMeansMainModule`.
3. У **Jobs → Start** задайте потрібні аргументи й натисніть **Submit**. Незадані параметри мають значення за замовчуванням:

| Параметр | Значення | Призначення |
|---|---|---|
| `PointsNumber` | `1` | Кількість воркерів паралельного модуля |
| `ClustersK` | `8` | Кількість кластерів, від 2 до 64 |
| `MaxIterations` | `40` | Максимальна кількість ітерацій |
| `ConvergenceThreshold` | `0.5` | Поріг максимального зсуву центроїдів |
| `UseSpatialFeatures` | `true` | `true` для 5D, `false` для 3D |
| `SpatialWeight` | `0.35` | Вага нормованих координат у 5D |
| `Seed` | `42` | Вибір початкових центроїдів |

4. Після **Completed** завантажте ZIP із колонки **Output**: `segmented.png` містить результат, `Output.txt` — метрики у форматі JSON. `Converged=false` означає завершення за лімітом ітерацій.

Після зміни коду повторіть збірку та завантажте модуль під новою назвою. Створіть Job для нової версії. Перебудова контейнерів потрібна лише після змін самої платформи PARCS.

## 3. Хмарний кластер

Використовується 1 Master з Host API, Portal, PostgreSQL і NFS та 8 ВМ із Daemon. Статичний режим задається `Hosting__Environment=Any`, `Scaling__Mode=Static`, `PointQueue__Provider=None`. Elasticsearch вимкнений; логи доступні через Docker Compose.

Для відтворення кластера підготуйте 9 ВМ Ubuntu 24.04 в одній мережі зі зв'язком між вузлами та скопіюйте репозиторій. У командах нижче замініть приклади приватних IP на фактичні адреси. На Master у каталозі `parcs8/infra/aws-ec2-static` виконайте:

```bash
sudo MASTER_IP=10.0.1.10 \
     WORKER_1_IP=10.0.1.11 WORKER_2_IP=10.0.1.12 \
     WORKER_3_IP=10.0.1.13 WORKER_4_IP=10.0.1.14 \
     WORKER_5_IP=10.0.1.15 WORKER_6_IP=10.0.1.16 \
     WORKER_7_IP=10.0.1.17 WORKER_8_IP=10.0.1.18 \
     bash setup-master.sh
```

На кожному воркері в тому самому каталозі, зі своєю адресою `WORKER_IP`:

```bash
sudo MASTER_IP=10.0.1.10 WORKER_IP=10.0.1.11 bash setup-worker.sh
```

Portal доступний за `http://PUBLIC_IP_MASTER/`. Збірка й запуск модуля описані в розділі 2.

## 4. Порівняння часу

Порівнюйте послідовний запуск із паралельними на 1, 2, 4 та 8 воркерах для того самого фото, `Seed` та решти параметрів. Для автоматичного прогону є [`run-benchmarks.ps1`](run-benchmarks.ps1). Спочатку зберіть модуль за розділом 2.

Для локального PARCS підставте шлях до свого фото:

```powershell
.\infra\aws-ec2-static\run-benchmarks.ps1 -HostApiUrl 'http://localhost:5000' `
    -ImagePath 'C:\Images\photo.jpg' -WorkerCounts @(1, 2, 4, 8)
```

Для хмарного кластера відкрийте SSH-тунель в окремому вікні:

```powershell
ssh -i 'шлях\до\ключа.pem' -N -L 5001:127.0.0.1:5000 ubuntu@PUBLIC_IP_MASTER
```

Потім виконайте ту саму команду бенчмарку з `-HostApiUrl 'http://localhost:5001'`. Для режиму 3D додайте `-UseSpatialFeatures $false`.

Скрипт зберігає ZIP і розпаковані результати в `infra/aws-ec2-static/benchmark-results/` та показує час, прискорення `S_p = T_seq / T_p` й ефективність `E_p = S_p / p`.

`ElapsedSeconds` включає ініціалізацію центроїдів, ітерації, фінальне призначення міток і формування RGB-зображення; у паралельному режимі також обмін даними з воркерами. Читання вхідного фото, кодування PNG, запис файлів і створення паралельних точок до цієї метрики не входять. Скрипт виконує один запуск кожного режиму; повторні вимірювання запускайте окремо. Локальні прогони слугують для перевірки, хмарні — для порівняння масштабування на різних ВМ.
