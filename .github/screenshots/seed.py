import json, os, sqlite3, sys, time
d = sys.argv[1]
os.makedirs(d, exist_ok=True)
json.dump({"WindowBounds": {"MainWindow": "100, 100, 900, 560",
                            "OverlayWindow": "300, 700, 650, 135"}},
          open(os.path.join(d, "setting.json"), "w"), ensure_ascii=False)
db = sqlite3.connect(os.path.join(d, "translation_history.db"))
db.executescript("""
CREATE TABLE IF NOT EXISTS TranslationHistory (Id INTEGER PRIMARY KEY AUTOINCREMENT, Timestamp TEXT, SourceText TEXT, TranslatedText TEXT, TargetLanguage TEXT, ApiUsed TEXT);
CREATE TABLE IF NOT EXISTS SectionSummary (Id INTEGER PRIMARY KEY AUTOINCREMENT, StartTime INTEGER, EndTime INTEGER, FirstHistoryId INTEGER, LastHistoryId INTEGER, Summary TEXT, PageNumber INTEGER);
CREATE TABLE IF NOT EXISTS Paragraph (Id INTEGER PRIMARY KEY AUTOINCREMENT, Time INTEGER, FirstHistoryId INTEGER, LastHistoryId INTEGER, Source TEXT, Translation TEXT);
CREATE TABLE IF NOT EXISTS Course (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, Created INTEGER);
CREATE TABLE IF NOT EXISTS Lecture (Id INTEGER PRIMARY KEY AUTOINCREMENT, CourseId INTEGER NOT NULL, Name TEXT, StartTime INTEGER, EndTime INTEGER, FirstHistoryId INTEGER, LastHistoryId INTEGER, FilePath TEXT);
""")
now = int(time.time()) - 3600
rows = [
 ("Today we will talk about gradient descent.", "今天我们来讲梯度下降。"),
 ("The idea is to move in the direction of steepest decrease of the loss.", "它的思想是沿着损失下降最快的方向移动。"),
 ("The learning rate controls how big each step is.", "学习率决定了每一步迈多大。"),
 ("If it is too large, the algorithm may overshoot the minimum.", "如果太大,算法可能会越过最小值。"),
 ("Stochastic gradient descent uses a small batch of samples at each step.", "随机梯度下降每一步只使用一小批样本。"),
 ("Next week we will look at momentum and Adam.", "下周我们会讲动量法和 Adam。"),
]
for i, (s, t) in enumerate(rows):
    db.execute("INSERT INTO TranslationHistory (Timestamp,SourceText,TranslatedText,TargetLanguage,ApiUsed) VALUES (?,?,?,?,?)",
               (str(now + i * 20), s, t, "zh-CN", "Demo"))
db.execute("INSERT INTO Course (Name,Created) VALUES ('机器学习',?)", (now,))
db.execute("INSERT INTO Lecture (CourseId,Name,StartTime,EndTime,FirstHistoryId,LastHistoryId,FilePath) VALUES (1,'第3讲 梯度下降',?,?,0,6,'')", (now, now + 120))
db.execute("INSERT INTO SectionSummary (StartTime,EndTime,FirstHistoryId,LastHistoryId,Summary,PageNumber) VALUES (?,?,1,3,'介绍梯度下降的基本思想,以及学习率对每一步步长的影响。',1)", (now, now + 40))
db.execute("INSERT INTO SectionSummary (StartTime,EndTime,FirstHistoryId,LastHistoryId,Summary,PageNumber) VALUES (?,?,4,6,'学习率过大会越过最小值;随机梯度下降使用小批量样本;下周讲动量法和 Adam。',2)", (now + 60, now + 100))
db.commit()
