# Паралельна сегментація зображень методом K-Means

Лабораторна робота з хмарних обчислень на C# / .NET 10 із використанням [PARCS.NET](https://github.com/alexeybogusevich/parcs8).

Реалізовано послідовну та розподілену сегментацію JPG/PNG у просторах RGB (3D) і RGB з координатами пікселів (5D). У паралельному режимі воркери обробляють частини зображення та повертають часткові суми для оновлення центрів кластерів.

## Код алгоритму

Модуль розташований у [`modules/Parcs.Modules.KMeansSegmentation`](modules/Parcs.Modules.KMeansSegmentation/):

- `Models/KMeansEngine.cs` — спільні обчислення K-Means;
- `Sequential/SequentialKMeansMainModule.cs` — послідовний запуск;
- `Parallel/ParallelKMeansMainModule.cs` — керуючий модуль;
- `Parallel/ParallelKMeansWorkerModule.cs` — обробка частини зображення на воркері.

## Запуск

[Інструкція](infra/aws-ec2-static/README.md) містить збірку модуля, локальну перевірку через PARCS у Docker Compose, налаштування хмарного кластера та порівняння часу на 1, 2, 4 і 8 воркерах.

Результат завдання — сегментоване зображення `segmented.png` та метрики у `Output.txt`.
