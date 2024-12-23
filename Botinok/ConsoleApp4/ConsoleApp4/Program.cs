using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    static ITelegramBotClient botClient;
    static Dictionary<long, List<Habit>> userHabits = new Dictionary<long, List<Habit>>();
    static Dictionary<long, TempHabit> tempHabits = new Dictionary<long, TempHabit>();
    static Dictionary<long, string> userStates = new Dictionary<long, string>(); 
    static HashSet<long> stoppedChats = new HashSet<long>();
    static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

    static async Task Main()
    {
        botClient = new TelegramBotClient(""); //токен

        Console.WriteLine($"Бот запущен: @{(await botClient.GetMeAsync()).Username}");

        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

       
        Task.Run(() => ResetHabitsDailyAsync(cancellationTokenSource.Token));
        Task.Run(() => ReminderNotificationsAsync(cancellationTokenSource.Token));

        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            },
            cancellationToken: cancellationToken
        );

        Console.WriteLine("Нажмите любую клавишу для остановки...");
        Console.ReadKey();

        cts.Cancel();
        cancellationTokenSource.Cancel();
    }

    static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        if (update.Type != UpdateType.Message || update.Message!.Type != MessageType.Text)
            return;

        var chatId = update.Message.Chat.Id;
        var messageText = update.Message.Text.ToLower();

       
        if (tempHabits.ContainsKey(chatId))
        {
            var tempHabit = tempHabits[chatId];

            if (tempHabit.CurrentStep == 1) 
            {
                tempHabit.Name = messageText;
                tempHabit.CurrentStep = 2;
                await bot.SendTextMessageAsync(chatId, "Введите цель привычки:", cancellationToken: cancellationToken);
                return;
            }
            else if (tempHabit.CurrentStep == 2) 
            {
                tempHabit.Goal = messageText;
                tempHabit.CurrentStep = 3;
                await bot.SendTextMessageAsync(chatId, "Выберите время напоминания:", replyMarkup: GetTimeKeyboard(), cancellationToken: cancellationToken);
                return;
            }
            else if (tempHabit.CurrentStep == 3) 
            {
                if (TimeSpan.TryParse(messageText, out TimeSpan reminderTime))
                {
                    tempHabit.ReminderTime = reminderTime;

                    if (!userHabits.ContainsKey(chatId))
                        userHabits[chatId] = new List<Habit>();

                    userHabits[chatId].Add(new Habit
                    {
                        Name = tempHabit.Name,
                        Goal = tempHabit.Goal,
                        ReminderTime = tempHabit.ReminderTime,
                        IsCompleted = false
                    });

                    tempHabits.Remove(chatId);

                    await bot.SendTextMessageAsync(chatId, "Привычка успешно добавлена!", replyMarkup: GetMainMenuKeyboard(), cancellationToken: cancellationToken);
                }
                else
                {
                    await bot.SendTextMessageAsync(chatId, "Неверный формат времени. Попробуйте снова:", replyMarkup: GetTimeKeyboard(), cancellationToken: cancellationToken);
                }
                return;
            }
        }

       
        if (userStates.ContainsKey(chatId))
        {
            var currentState = userStates[chatId];

            if (currentState == "delete_habit")
            {
                if (int.TryParse(messageText, out int deleteIndex) && userHabits.ContainsKey(chatId) && deleteIndex > 0 && deleteIndex <= userHabits[chatId].Count)
                {
                    var habit = userHabits[chatId][deleteIndex - 1];
                    userHabits[chatId].RemoveAt(deleteIndex - 1);

                    await bot.SendTextMessageAsync(chatId, $"Привычка \"{habit.Name}\" удалена.", cancellationToken: cancellationToken);
                }
                else
                {
                    await bot.SendTextMessageAsync(chatId, "Неверный номер привычки. Попробуйте снова.", cancellationToken: cancellationToken);
                }

                userStates.Remove(chatId);
                return;
            }
            else if (currentState == "mark_habit")
            {
                if (int.TryParse(messageText, out int markIndex) && userHabits.ContainsKey(chatId) && markIndex > 0 && markIndex <= userHabits[chatId].Count)
                {
                    var habit = userHabits[chatId][markIndex - 1];
                    habit.IsCompleted = !habit.IsCompleted;

                    var status = habit.IsCompleted ? "✅ Выполнено!" : "❌ Не выполнено!";
                    await bot.SendTextMessageAsync(chatId, $"Привычка \"{habit.Name}\": {status}", cancellationToken: cancellationToken);
                }
                else
                {
                    await bot.SendTextMessageAsync(chatId, "Неверный номер привычки. Попробуйте снова.", cancellationToken: cancellationToken);
                }

                userStates.Remove(chatId);
                return;
            }
        }

        
        if (messageText == "/start")
        {
            stoppedChats.Remove(chatId);
            await bot.SendTextMessageAsync(
                chatId: chatId,
                text: "Добро пожаловать в трекер привычек!",
                replyMarkup: GetMainMenuKeyboard(),
                cancellationToken: cancellationToken
            );
        }
        else if (messageText == "/stop")
        {
            stoppedChats.Add(chatId);
            await bot.SendTextMessageAsync(chatId, "До свидания!", cancellationToken: cancellationToken);
        }
        else if (messageText == "добавить привычку")
        {
            tempHabits[chatId] = new TempHabit { CurrentStep = 1 };
            await bot.SendTextMessageAsync(chatId, "Введите название привычки:", cancellationToken: cancellationToken);
        }
        else if (messageText == "посмотреть привычки")
        {
            if (!userHabits.ContainsKey(chatId) || userHabits[chatId].Count == 0)
            {
                await bot.SendTextMessageAsync(chatId, "У вас пока нет привычек.", cancellationToken: cancellationToken);
                return;
            }

            var habits = userHabits[chatId];
            string response = "Ваши привычки:\n";

            for (int i = 0; i < habits.Count; i++)
            {
                var status = habits[i].IsCompleted ? "✅" : "❌";
                response += $"{i + 1}. {habits[i].Name}: {habits[i].Goal} (Напоминание: {habits[i].ReminderTime}) {status}\n";
            }

            await bot.SendTextMessageAsync(chatId, response, cancellationToken: cancellationToken);
        }
        else if (messageText == "удалить привычку")
        {
            if (!userHabits.ContainsKey(chatId) || userHabits[chatId].Count == 0)
            {
                await bot.SendTextMessageAsync(chatId, "У вас пока нет привычек для удаления.", cancellationToken: cancellationToken);
                return;
            }

            var habits = userHabits[chatId];
            string response = "Введите номер привычки для удаления:\n";

            for (int i = 0; i < habits.Count; i++)
            {
                response += $"{i + 1}. {habits[i].Name}: {habits[i].Goal} (Напоминание: {habits[i].ReminderTime})\n";
            }

            userStates[chatId] = "delete_habit";
            await bot.SendTextMessageAsync(chatId, response, cancellationToken: cancellationToken);
        }
        else if (messageText == "отметить выполнение")
        {
            if (!userHabits.ContainsKey(chatId) || userHabits[chatId].Count == 0)
            {
                await bot.SendTextMessageAsync(chatId, "У вас нет привычек для выполнения.", cancellationToken: cancellationToken);
                return;
            }

            var habits = userHabits[chatId];
            string response = "Введите номер привычки для отметки:\n";

            for (int i = 0; i < habits.Count; i++)
            {
                response += $"{i + 1}. {habits[i].Name} ({habits[i].Goal})\n";
            }

            userStates[chatId] = "mark_habit";
            await bot.SendTextMessageAsync(chatId, response, cancellationToken: cancellationToken);
        }
        else
        {
            await bot.SendTextMessageAsync(chatId, "Неизвестная команда.", cancellationToken: cancellationToken);
        }
    }

    static Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Ошибка: {exception.Message}");
        return Task.CompletedTask;
    }

    static async Task ResetHabitsDailyAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextReset = now.Date.AddDays(1);

            await Task.Delay(nextReset - now, cancellationToken);

            foreach (var user in userHabits.Values)
            {
                foreach (var habit in user)
                {
                    habit.IsCompleted = false;
                }
            }

            Console.WriteLine("Все привычки сброшены.");
        }
    }

    static async Task ReminderNotificationsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTime.Now.TimeOfDay;

            foreach (var chatHabits in userHabits)
            {
                var chatId = chatHabits.Key;

                foreach (var habit in chatHabits.Value)
                {
                    if (habit.ReminderTime.Hours == now.Hours && habit.ReminderTime.Minutes == now.Minutes && !habit.IsCompleted)
                    {
                        await botClient.SendTextMessageAsync(chatId, $"Напоминание: Пора заняться привычкой \"{habit.Name}\" ({habit.Goal})!");
                    }
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }
    }

    static IReplyMarkup GetMainMenuKeyboard()
    {
        return new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Добавить привычку", "Посмотреть привычки" },
            new KeyboardButton[] { "Удалить привычку", "Отметить выполнение" }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = false
        };
    }

    static IReplyMarkup GetTimeKeyboard()
    {
        var rows = new List<KeyboardButton[]>();

        for (int hour = 0; hour < 24; hour++)
        {
            var row = new List<KeyboardButton>();
            row.Add(new KeyboardButton($"{hour:D2}:00"));
            row.Add(new KeyboardButton($"{hour:D2}:30"));
            rows.Add(row.ToArray());
        }

        return new ReplyKeyboardMarkup(rows)
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };
    }

    class TempHabit
    {
        public int CurrentStep { get; set; }
        public string Name { get; set; }
        public string Goal { get; set; }
        public TimeSpan ReminderTime { get; set; }
    }

    class Habit
    {
        public string Name { get; set; }
        public string Goal { get; set; }
        public TimeSpan ReminderTime { get; set; }
        public bool IsCompleted { get; set; }
    }
}
